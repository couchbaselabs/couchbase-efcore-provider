// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Constant = System.Linq.Expressions.ConstantExpression;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitor : RelationalShapedQueryCompilingExpressionVisitor
{
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly ISet<string> _tags;
    private readonly Type _contextType;
    private readonly bool _threadSafetyChecksEnabled;
    private readonly bool _detailedErrorsEnabled;

    /// <summary>
    ///     Creates a new instance of the <see cref="ShapedQueryCompilingExpressionVisitor" /> class.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this class.</param>
    /// <param name="relationalDependencies">Parameter object containing relational dependencies for this class.</param>
    /// <param name="queryCompilationContext">The query compilation context object to use.</param>
    public CouchbaseShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        QueryCompilationContext queryCompilationContext,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _tags = queryCompilationContext.Tags;
        _contextType = queryCompilationContext.ContextType;
        _threadSafetyChecksEnabled = dependencies.CoreSingletonOptions.AreThreadSafetyChecksEnabled;
        _detailedErrorsEnabled = dependencies.CoreSingletonOptions.AreDetailedErrorsEnabled;
    }

    /// <summary>
    /// Walks the shaper expression and records root-level <see cref="NavigationInclude"/> nodes
    /// (those with a concrete <see cref="INavigation"/>) into the compilation context.
    /// ThenInclude chains are embedded inside collection/reference shaper expressions and are
    /// resolved during Phase 4.
    /// </summary>
    private void CollectNavigationIncludes(Expression shaperExpression)
    {
        if (QueryCompilationContext is not CouchbaseQueryCompilationContext ctx)
            return;

        PopulateNavigationIncludes(shaperExpression, ctx.NavigationIncludes);
    }

    /// <summary>
    /// Populates <paramref name="target"/> with root-level <see cref="NavigationInclude"/> nodes
    /// extracted from <paramref name="shaperExpression"/>.
    /// Exposed as <c>internal</c> for unit testing without a live compilation context.
    /// </summary>
    internal static void PopulateNavigationIncludes(Expression shaperExpression, List<NavigationInclude> target)
        => target.AddRange(ExtractNavigationIncludes(shaperExpression));

    /// <summary>
    /// Extracts root-level <see cref="NavigationInclude"/> nodes from a shaper expression's
    /// <see cref="IncludeExpression"/> chain, in original Include order.
    /// Exposed as <c>internal</c> for unit testing.
    /// </summary>
    internal static List<NavigationInclude> ExtractNavigationIncludes(Expression shaperExpression)
    {
        var current = shaperExpression;
        var collected = new List<NavigationInclude>();

        while (current is IncludeExpression include)
        {
            // Both INavigation (FK-based) and ISkipNavigation (HasMany/WithMany) are recorded.
            // The EF Core shaper handles collection accumulation for both via the standard
            // SingleQueryResultCoordinator path — no custom population code is needed.
            //
            // Filter is always null here: by the time VisitShapedQuery runs, any filter
            // lambda from a filtered include (.Include(b => b.Posts.Where(...))) has already
            // been translated into a SQL predicate inside the SelectExpression and is no
            // longer recoverable as a LambdaExpression from IncludeExpression.NavigationExpression.
            // Filtered includes must be detected by inspecting RelationalCollectionShaperExpression
            // or the inner SelectExpression's WHERE clause rather than relying on Filter here.
            if (include.Navigation is INavigation nav)
                collected.Add(new NavigationInclude(nav, null, ExtractChildren(include.NavigationExpression)));
            else if (include.Navigation is ISkipNavigation skipNav)
                collected.Add(new NavigationInclude(skipNav, null, ExtractChildren(include.NavigationExpression)));

            current = include.EntityExpression;
        }

        // IncludeExpressions are chained outermost-first, so traversal builds the list in
        // reverse Include order. One Reverse() restores original order — O(n) vs O(n²) Insert(0).
        collected.Reverse();
        return collected;
    }

    // Extracts ThenInclude children from the NavigationExpression of a single IncludeExpression.
    // Collection navigations wrap their inner shaper in RelationalCollectionShaperExpression;
    // reference navigations embed ThenInclude nodes directly as a nested IncludeExpression.
    private static List<NavigationInclude> ExtractChildren(Expression navigationExpression)
    {
        if (navigationExpression is RelationalCollectionShaperExpression collectionShaper)
            return ExtractNavigationIncludes(collectionShaper.InnerShaper);

        return ExtractNavigationIncludes(navigationExpression);
    }

    /// <inheritdoc />
    [Experimental("EF9100")]
    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        CollectNavigationIncludes(shapedQueryExpression.ShaperExpression);

        var selectExpression = (SelectExpression)shapedQueryExpression.QueryExpression;

        VerifyNoClientConstant(shapedQueryExpression.ShaperExpression);
        var querySplittingBehavior = ((RelationalQueryCompilationContext)QueryCompilationContext).QuerySplittingBehavior;
        var splitQuery = querySplittingBehavior == QuerySplittingBehavior.SplitQuery;
        var collectionCount = 0;

        if (shapedQueryExpression.ShaperExpression is RelationalGroupByResultExpression relationalGroupByResultExpression)
        {
            var elementSelector = new ShaperProcessingExpressionVisitor(this, selectExpression, _tags, splitQuery, indexMap: false)
                .ProcessRelationalGroupingResult(
                    relationalGroupByResultExpression,
                    out var relationalCommandResolver,
                    out var readerColumns,
                    out var keySelector,
                    out var keyIdentifier,
                    out var relatedDataLoaders,
                    ref collectionCount);

            if (querySplittingBehavior == null
                && collectionCount > 1)
            {
                QueryCompilationContext.Logger.MultipleCollectionIncludeWarning();
            }

            var readerColumnsExpression = CreateReaderColumnsExpression(readerColumns, Dependencies.LiftableConstantFactory);

            return Expression.Call(
                CreateGroupBySingleQueryingEnumerableMethodInfo.MakeGenericMethod(keySelector.ReturnType, elementSelector.ReturnType),
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
                relationalCommandResolver,
                readerColumnsExpression,
                keySelector,
                keyIdentifier,
                Dependencies.LiftableConstantFactory.CreateLiftableConstant(
                    relationalGroupByResultExpression.KeyIdentifierValueComparers.Select(x => (Func<object, object, bool>)x.Equals)
                        .ToArray(),
#pragma warning disable EF9100
                    Expression.Lambda<Func<MaterializerLiftableConstantContext, object>>(
#pragma warning restore EF9100
                        Expression.NewArrayInit(
                            typeof(Func<object, object, bool>),
                            relationalGroupByResultExpression.KeyIdentifierValueComparers.Select(vc => vc.ObjectEqualsExpression)),

#pragma warning disable EF9100
                        Expression.Parameter(typeof(MaterializerLiftableConstantContext), "_")),
#pragma warning restore EF9100

                    "keyIdentifierValueComparers",
                    typeof(Func<object, object, bool>[])),
                elementSelector,
                Expression.Constant(_contextType),
                Expression.Constant(QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.NoTrackingWithIdentityResolution),
                Expression.Constant(_detailedErrorsEnabled),
                Expression.Constant(_threadSafetyChecksEnabled));
        }
        else
        {
            var nonComposedFromSql = selectExpression.IsNonComposedFromSql();
            var shaper = new ShaperProcessingExpressionVisitor(this, selectExpression, _tags, splitQuery, nonComposedFromSql)
                .ProcessShaper(
                    shapedQueryExpression.ShaperExpression, out var relationalCommandResolver, out var readerColumns,
                    out var relatedDataLoaders, ref collectionCount);

            if (querySplittingBehavior == null
                && collectionCount > 1)
            {
                QueryCompilationContext.Logger.MultipleCollectionIncludeWarning();
            }

            var readerColumnsExpression = CreateReaderColumnsExpression(readerColumns, Dependencies.LiftableConstantFactory);
            if (nonComposedFromSql)
            {
                return Expression.Call(
                    CreateFromSqlQueryingEnumerableMethodInfo.MakeGenericMethod(shaper.ReturnType),
                    Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
                    relationalCommandResolver,
                    readerColumnsExpression,
                    Dependencies.LiftableConstantFactory.CreateLiftableConstant(
                        selectExpression.Projection.Select(pe => ((ColumnExpression)pe.Expression).Name).ToArray(),
#pragma warning disable EF9100
                        Expression.Lambda<Func<MaterializerLiftableConstantContext, object>>(
#pragma warning restore EF9100
                            Expression.NewArrayInit(
                                typeof(string),
                                selectExpression.Projection.Select(pe => Expression.Constant(((ColumnExpression)pe.Expression).Name, typeof(string)))),
#pragma warning disable EF9100
                            Expression.Parameter(typeof(MaterializerLiftableConstantContext), "_")),
#pragma warning restore EF9100
                        "columnNames",
                        typeof(string[])),
                    shaper,
                    Expression.Constant(_contextType),
                    Expression.Constant(QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.NoTrackingWithIdentityResolution),
                    Expression.Constant(_detailedErrorsEnabled),
                    Expression.Constant(_threadSafetyChecksEnabled),
                    Expression.Constant(_bucketProvider),
                    Expression.Constant(_couchbaseDbContextOptionsBuilder));
            }

            // Add OwnsMany/OwnsOne navigation columns to the SELECT projection using the IR so
            // the embedded JSON arrives in the N1QL row. Done after ProcessShaper (shaper
            // ordinals are already fixed) and before projectionAliases is built (so the new
            // names are included in the alias array that CouchbaseDbDataReader uses for column
            // lookup).
            var ownedNavProjections = AddOwnedNavigationColumnsToProjection(selectExpression, shaper.ReturnType);

            // Build the per-ordinal N1QL response keys.  Aliases are made unique (see
            // CouchbaseProjectionAliases) so that a collection Include where the principal and
            // dependent share a property name (e.g. blogId / rating) maps each shaper ordinal to
            // a distinct JSON key instead of both reading the same colliding key.  The SQL
            // generator derives the same array from the same projection list, so the emitted
            // AS clauses and this array stay aligned.
            //
            // The FULL unfiltered projection is kept so the EF Core shaper's baked-in ordinals
            // remain correct. Columns from skipped owned-type JOINs (e.g. cm0.id) are absent from
            // the N1QL result and return DBNull via CouchbaseDbDataReader, which is the expected
            // signal for "no collection rows" — PopulateCollectionNavigations then fills the
            // actual collection from the embedded JSON array.
            var uniqueAliases = CouchbaseProjectionAliases.ComputeUnique(selectExpression.Projection);
            var projectionAliasesExpression = CreateStringArrayLiftableConstant(uniqueAliases, "projectionAliases");

            // Resolve each owned navigation's FINAL (post-uniquification) alias, so
            // CouchbaseOwnedCollectionMaterializer can read the correct JSON key even when its
            // field name collided with another projected column and was suffixed (e.g.
            // "address" → "address0"). Keyed by CouchbaseProjectionAliases.NavigationKey rather
            // than the field name itself, since that's exactly what may have changed.
            var ownedNavKeys = new string[ownedNavProjections.Count];
            var ownedNavAliases = new string[ownedNavProjections.Count];
            for (var i = 0; i < ownedNavProjections.Count; i++)
            {
                ownedNavKeys[i] = CouchbaseProjectionAliases.NavigationKey(ownedNavProjections[i].Navigation);
                ownedNavAliases[i] = uniqueAliases[ownedNavProjections[i].ProjectionIndex];
            }
            var ownedNavKeysExpression = CreateStringArrayLiftableConstant(ownedNavKeys, "ownedNavKeys");
            var ownedNavAliasesExpression = CreateStringArrayLiftableConstant(ownedNavAliases, "ownedNavAliases");

            return Expression.Call(
                CreateSingleQueryingEnumerableMethodInfo.MakeGenericMethod(shaper.ReturnType),
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
                relationalCommandResolver,
                readerColumnsExpression,
                projectionAliasesExpression,
                ownedNavKeysExpression,
                ownedNavAliasesExpression,
                shaper,
                Expression.Constant(_contextType),
                Expression.Constant(QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.NoTrackingWithIdentityResolution),
                Expression.Constant(QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.TrackAll),
                Expression.Constant(_detailedErrorsEnabled),
                Expression.Constant(_threadSafetyChecksEnabled),
                Expression.Constant(_bucketProvider),
                Expression.Constant(_couchbaseDbContextOptionsBuilder));
        }
    }

    /// <summary>
    /// Appends a <see cref="ColumnExpression"/> for each OwnsMany navigation, and each root-level
    /// OwnsOne navigation, of the root entity to <paramref name="selectExpression"/>'s projection
    /// so the embedded JSON (array or object) arrives inline with the N1QL result row. This
    /// replaces the post-hoc string rewriting that <c>InjectOwnedCollectionColumns</c> previously
    /// applied to the emitted SQL, eliminating brittleness caused by matching FROM/AS tokens in
    /// string literals or nested subqueries.
    /// <para>
    /// OwnsMany relies on this raw JSON for materialization entirely (no JOIN is emitted).
    /// OwnsOne still gets its usual flat table-split columns from EF Core's default relational
    /// projection — this extra column is an additive fallback so
    /// <see cref="CouchbaseOwnedCollectionMaterializer.PopulateReference"/> can override the
    /// flat-column result when the navigation's data is actually stored as a genuinely nested
    /// JSON object (e.g. a foreign/pre-existing document) rather than this provider's own flat
    /// write scheme.
    /// </para>
    ///
    /// Handles two shapes that EF Core emits for entities with collection includes:
    /// <list type="bullet">
    ///   <item>Simple — FROM references the entity <see cref="TableExpression"/> directly.</item>
    ///   <item>Subquery — FROM references an inner <see cref="SelectExpression"/> (used when
    ///     EF Core wraps a LIMIT-constrained query); the column is added to both the inner and
    ///     outer projections using their respective aliases.</item>
    /// </list>
    /// </summary>
    /// <returns>
    /// For each owned navigation whose column was appended, the navigation paired with the
    /// index of its <em>outer</em> <paramref name="selectExpression"/> projection entry. The
    /// alias at that index is only final once <see cref="CouchbaseProjectionAliases.ComputeUnique"/>
    /// runs over the completed projection list — a colliding field name (e.g. two owned
    /// navigations, or an owned navigation and an unrelated projected column, that both happen to
    /// resolve to the same effective alias) gets suffixed at that point, and the raw N1QL result
    /// row is keyed by that final, possibly-suffixed alias rather than the field name computed
    /// here. Callers must resolve each navigation's actual key from the index returned rather
    /// than assuming it equals the field name this method used, or a colliding fallback column
    /// would silently fail to materialise.
    /// </returns>
    private List<(INavigation Navigation, int ProjectionIndex)> AddOwnedNavigationColumnsToProjection(
        SelectExpression selectExpression, Type shaperReturnType)
    {
        var result = new List<(INavigation Navigation, int ProjectionIndex)>();

        // Resolve the entity type first so we can match the TableExpression by the entity's
        // configured table name rather than the fragile 3-part "bucket.scope.collection"
        // heuristic that fails silently in test contexts where SetupKeyspaces is not called.
        var entityType = QueryCompilationContext.Model.GetEntityTypes()
            .FirstOrDefault(e => e.ClrType == shaperReturnType);
        if (entityType == null) return result;

        var expectedTableName = entityType.GetTableName();
        if (expectedTableName == null) return result;

        // Locate the owner entity's TableExpression by matching its configured name.
        // Simple form:   Tables contains the entity TableExpression directly.
        // Subquery form: Tables[0] is an inner SelectExpression (EF Core wraps FirstAsync/Take
        //                in a subquery before the LEFT JOIN expansion); the entity TableExpression
        //                is inside the inner select.
        TableExpression? ownerTable = null;
        SelectExpression? innerSelect = null;

        var directTable = selectExpression.Tables
            .OfType<TableExpression>()
            .FirstOrDefault(t => t.Name == expectedTableName);

        if (directTable != null)
        {
            ownerTable = directTable;
        }
        else
        {
            innerSelect = selectExpression.Tables.OfType<SelectExpression>().FirstOrDefault();
            if (innerSelect != null)
                ownerTable = innerSelect.Tables
                    .OfType<TableExpression>()
                    .FirstOrDefault(t => t.Name == expectedTableName);
        }

        if (ownerTable == null) return result;

        // Include owned navigations declared on derived types (TPH): when the base type is
        // queried, the derived rows' owned field must still be projected so it lands in the
        // result JSON for CouchbaseQueryEnumerable to materialise. Derived types share the
        // owner's table, so the column reference against ownerTable is valid (null for base rows).
        // Covers both OwnsMany (arrays, relied on exclusively) and root-level OwnsOne (objects,
        // used only as a fallback — see the PopulateReference reference in the doc comment above).
        var ownedNavs = entityType.GetNavigations()
            .Concat(entityType.GetDerivedTypes().SelectMany(t => t.GetDeclaredNavigations()))
            .Where(n => n.TargetEntityType.IsOwned())
            .ToList();
        if (ownedNavs.Count == 0) return result;

        var policy = _couchbaseDbContextOptionsBuilder.FieldNamingPolicy;
        foreach (var nav in ownedNavs)
        {
            var fieldName = policy?.ConvertName(nav.Name) ?? nav.Name;

            if (innerSelect != null)
            {
                // Subquery form: project the column from the entity table into the inner SELECT,
                // then surface it from the inner subquery's alias in the outer SELECT.
                innerSelect.AddToProjection(
                    new ColumnExpression(fieldName, ownerTable.Alias, typeof(object), null, true));
                selectExpression.AddToProjection(
                    new ColumnExpression(fieldName, innerSelect.Alias!, typeof(object), null, true));
            }
            else
            {
                selectExpression.AddToProjection(
                    new ColumnExpression(fieldName, ownerTable.Alias, typeof(object), null, true));
            }

            // AddToProjection always appends, so the entry we just added to the OUTER
            // selectExpression's projection is the last one, regardless of the simple/subquery
            // form above.
            result.Add((nav, selectExpression.Projection.Count - 1));
        }

        return result;
    }

    /// <summary>
    /// Builds a liftable <c>string[]</c> constant for the compiled query — the same pattern used
    /// for <c>projectionAliases</c>, now shared with the owned-navigation alias-resolution arrays.
    /// </summary>
    [Experimental("EF9100")]
    private Expression CreateStringArrayLiftableConstant(string[] values, string parameterName)
        => Dependencies.LiftableConstantFactory.CreateLiftableConstant(
            values,
#pragma warning disable EF9100
            Expression.Lambda<Func<MaterializerLiftableConstantContext, object>>(
#pragma warning restore EF9100
                Expression.NewArrayInit(
                    typeof(string),
                    values.Select(a => (Expression)Expression.Constant(a, typeof(string)))),
#pragma warning disable EF9100
                Expression.Parameter(typeof(MaterializerLiftableConstantContext), "_")),
#pragma warning restore EF9100
            parameterName,
            typeof(string[]));

    [Experimental("EF9100")]
    private static Expression CreateReaderColumnsExpression(
        IReadOnlyList<ReaderColumn?>? readerColumns,
        ILiftableConstantFactory liftableConstantFactory)
    {
        if (readerColumns is null)
        {
            return Expression.Constant(readerColumns, typeof(ReaderColumn?[]));
        }

#pragma warning disable EF9100
        var materializerLiftableConstantContextParameter = Expression.Parameter(typeof(MaterializerLiftableConstantContext));
#pragma warning restore EF9100
        var initializers = new List<Expression>();

        foreach (var readerColumn in readerColumns)
        {
            var currentReaderColumn = readerColumn;
            if (currentReaderColumn is null)
            {
                initializers.Add(Expression.Constant(null, typeof(ReaderColumn)));
                continue;
            }

            var propertyExpression = LiftableConstantExpressionHelpers.BuildMemberAccessForProperty(
                currentReaderColumn.Property,
                materializerLiftableConstantContextParameter);

            initializers.Add(
               Expression.New(
                    ReaderColumn.GetConstructor(currentReaderColumn.Type),
                    Expression.Constant(currentReaderColumn.IsNullable),
                    Expression.Constant(currentReaderColumn.Name, typeof(string)),
                    propertyExpression,
                    currentReaderColumn.GetFieldValueExpression));
        }

        var result = liftableConstantFactory.CreateLiftableConstant(
            readerColumns,
#pragma warning disable EF9100
            Expression.Lambda<Func<MaterializerLiftableConstantContext, object>>(
#pragma warning restore EF9100
                Expression.NewArrayInit(
                    typeof(ReaderColumn),
                    initializers),
                materializerLiftableConstantContextParameter),
            "readerColumns",
            typeof(ReaderColumn[]));

        return result;
    }

    private static readonly MethodInfo CreateGroupBySingleQueryingEnumerableMethodInfo
        = typeof(GroupBySingleQueryingEnumerable)
            .GetMethod(nameof(GroupBySingleQueryingEnumerable.Create))!;

    private static readonly MethodInfo CreateFromSqlQueryingEnumerableMethodInfo
        = typeof(CouchbaseFromSqlQueryingEnumerable)
            .GetMethod(nameof(CouchbaseFromSqlQueryingEnumerable.Create))!;

    private static readonly MethodInfo CreateSingleQueryingEnumerableMethodInfo
        = typeof(CouchbaseQueryEnumerable)
            .GetMethod(nameof(CouchbaseQueryEnumerable.Create))!;
}
