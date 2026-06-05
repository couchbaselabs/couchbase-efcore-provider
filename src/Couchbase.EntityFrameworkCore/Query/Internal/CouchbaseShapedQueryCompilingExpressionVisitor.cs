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

            // Add OwnsMany navigation columns to the SELECT projection using the IR so the
            // embedded arrays arrive in the N1QL row. Done after ProcessShaper (shaper ordinals
            // are already fixed) and before projectionAliases is built (so the new names are
            // included in the alias array that CouchbaseDbDataReader uses for column lookup).
            AddOwnedCollectionColumnsToProjection(selectExpression, shaper.ReturnType);

            // ProjectionExpression.Alias is "" when no explicit AS clause is emitted — in that case
            // the N1QL response key is the underlying ColumnExpression.Name.
            static string EffectiveAlias(ProjectionExpression pe) =>
                pe.Alias != string.Empty ? pe.Alias
                    : pe.Expression is ColumnExpression c ? c.Name
                    : string.Empty;

            // Keep the FULL unfiltered projection alias array so the EF Core shaper's baked-in
            // ordinals remain correct. Columns from skipped owned-type JOINs (e.g. cm0.id) are
            // absent from the N1QL result and return DBNull via CouchbaseDbDataReader, which is
            // the expected signal for "no collection rows" — PopulateCollectionNavigations then
            // fills the actual collection from the embedded JSON array.
            var projectionAliasesExpression = Dependencies.LiftableConstantFactory.CreateLiftableConstant(
                selectExpression.Projection.Select(EffectiveAlias).ToArray(),
#pragma warning disable EF9100
                Expression.Lambda<Func<MaterializerLiftableConstantContext, object>>(
#pragma warning restore EF9100
                    Expression.NewArrayInit(
                        typeof(string),
                        selectExpression.Projection.Select(pe => Expression.Constant(EffectiveAlias(pe), typeof(string)))),
#pragma warning disable EF9100
                    Expression.Parameter(typeof(MaterializerLiftableConstantContext), "_")),
#pragma warning restore EF9100
                "projectionAliases",
                typeof(string[]));

            return Expression.Call(
                CreateSingleQueryingEnumerableMethodInfo.MakeGenericMethod(shaper.ReturnType),
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
                relationalCommandResolver,
                readerColumnsExpression,
                projectionAliasesExpression,
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
    /// Appends a <see cref="ColumnExpression"/> for each OwnsMany navigation of the root entity
    /// to <paramref name="selectExpression"/>'s projection so the embedded JSON arrays arrive
    /// inline with the N1QL result row. This replaces the post-hoc string rewriting that
    /// <c>InjectOwnedCollectionColumns</c> previously applied to the emitted SQL, eliminating
    /// brittleness caused by matching FROM/AS tokens in string literals or nested subqueries.
    ///
    /// Handles two shapes that EF Core emits for entities with collection includes:
    /// <list type="bullet">
    ///   <item>Simple — FROM references the entity <see cref="TableExpression"/> directly.</item>
    ///   <item>Subquery — FROM references an inner <see cref="SelectExpression"/> (used when
    ///     EF Core wraps a LIMIT-constrained query); the column is added to both the inner and
    ///     outer projections using their respective aliases.</item>
    /// </list>
    /// </summary>
    private void AddOwnedCollectionColumnsToProjection(SelectExpression selectExpression, Type shaperReturnType)
    {
        // Resolve the entity type first so we can match the TableExpression by the entity's
        // configured table name rather than the fragile 3-part "bucket.scope.collection"
        // heuristic that fails silently in test contexts where SetupKeyspaces is not called.
        var entityType = QueryCompilationContext.Model.GetEntityTypes()
            .FirstOrDefault(e => e.ClrType == shaperReturnType);
        if (entityType == null) return;

        var expectedTableName = entityType.GetTableName();
        if (expectedTableName == null) return;

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

        if (ownerTable == null) return;

        var ownedCollNavs = entityType.GetNavigations()
            .Where(n => n.IsCollection && n.TargetEntityType.IsOwned())
            .ToList();
        if (ownedCollNavs.Count == 0) return;

        var policy = _couchbaseDbContextOptionsBuilder.FieldNamingPolicy;
        foreach (var nav in ownedCollNavs)
        {
            var fieldName = policy?.ConvertName(nav.Name) ?? nav.Name;

            if (innerSelect != null)
            {
                // Subquery form: project the column from the entity table into the inner SELECT,
                // then surface it from the inner subquery's alias in the outer SELECT.
                innerSelect.AddToProjection(
                    new ColumnExpression(fieldName, ownerTable.Alias, typeof(object), null, true));
                selectExpression.AddToProjection(
                    new ColumnExpression(fieldName, innerSelect.Alias, typeof(object), null, true));
            }
            else
            {
                selectExpression.AddToProjection(
                    new ColumnExpression(fieldName, ownerTable.Alias, typeof(object), null, true));
            }
        }
    }

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
