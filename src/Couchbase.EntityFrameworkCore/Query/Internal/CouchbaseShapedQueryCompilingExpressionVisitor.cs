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

    /// <inheritdoc />
    [Experimental("EF9100")]
    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
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

            return Expression.Call(
                CreateSingleQueryingEnumerableMethodInfo.MakeGenericMethod(shaper.ReturnType),
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
                relationalCommandResolver,
                readerColumnsExpression,
                shaper,
                Expression.Constant(_contextType),
                Expression.Constant(QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.NoTrackingWithIdentityResolution),
                Expression.Constant(_detailedErrorsEnabled),
                Expression.Constant(_threadSafetyChecksEnabled),
                Expression.Constant(_bucketProvider),
                Expression.Constant(_couchbaseDbContextOptionsBuilder));
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
