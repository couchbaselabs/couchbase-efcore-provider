using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryableMethodTranslatingExpressionVisitor : QueryableMethodTranslatingExpressionVisitor
{
    public CouchbaseQueryableMethodTranslatingExpressionVisitor(QueryableMethodTranslatingExpressionVisitorDependencies dependencies, QueryCompilationContext queryCompilationContext, bool subquery) 
        : base(dependencies, queryCompilationContext, subquery)
    {
    }

    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor()
    {
        throw new NotImplementedException();
    }
    
    protected override ShapedQueryExpression CreateShapedQueryExpression(IEntityType entityType)
    {
        var queryExpression = new CouchbaseQueryExpression(entityType);
        return new ShapedQueryExpression(
            queryExpression,
            new StructuralTypeShaperExpression(
                entityType,
                new ProjectionBindingExpression(queryExpression, new ProjectionMember(), typeof(ValueBuffer)),
                false));
    }

    protected override ShapedQueryExpression? TranslateAll(ShapedQueryExpression source, LambdaExpression predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateAny(ShapedQueryExpression source, LambdaExpression? predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateAverage(ShapedQueryExpression source, LambdaExpression? selector, Type resultType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateCast(ShapedQueryExpression source, Type castType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateConcat(ShapedQueryExpression source1, ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateContains(ShapedQueryExpression source, Expression item)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateCount(ShapedQueryExpression source, LambdaExpression? predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateDefaultIfEmpty(ShapedQueryExpression source, Expression? defaultValue)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateDistinct(ShapedQueryExpression source)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateElementAtOrDefault(ShapedQueryExpression source, Expression index, bool returnDefault)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateExcept(ShapedQueryExpression source1, ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateFirstOrDefault(ShapedQueryExpression source, LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateGroupBy(ShapedQueryExpression source, LambdaExpression keySelector,
        LambdaExpression? elementSelector, LambdaExpression? resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateGroupJoin(ShapedQueryExpression outer, ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateIntersect(ShapedQueryExpression source1, ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateJoin(ShapedQueryExpression outer, ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateLeftJoin(ShapedQueryExpression outer, ShapedQueryExpression inner,
        LambdaExpression outerKeySelector, LambdaExpression innerKeySelector, LambdaExpression resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateLastOrDefault(ShapedQueryExpression source, LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateLongCount(ShapedQueryExpression source, LambdaExpression? predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateMax(ShapedQueryExpression source, LambdaExpression? selector, Type resultType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateMin(ShapedQueryExpression source, LambdaExpression? selector, Type resultType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateOfType(ShapedQueryExpression source, Type resultType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateOrderBy(ShapedQueryExpression source, LambdaExpression keySelector, bool ascending)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateReverse(ShapedQueryExpression source)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression TranslateSelect(ShapedQueryExpression source, LambdaExpression selector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSelectMany(ShapedQueryExpression source, LambdaExpression collectionSelector,
        LambdaExpression resultSelector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSelectMany(ShapedQueryExpression source, LambdaExpression selector)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSingleOrDefault(ShapedQueryExpression source, LambdaExpression? predicate,
        Type returnType, bool returnDefault)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSkip(ShapedQueryExpression source, Expression count)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSkipWhile(ShapedQueryExpression source, LambdaExpression predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateSum(ShapedQueryExpression source, LambdaExpression? selector, Type resultType)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateTake(ShapedQueryExpression source, Expression count)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateTakeWhile(ShapedQueryExpression source, LambdaExpression predicate)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateThenBy(ShapedQueryExpression source, LambdaExpression keySelector, bool ascending)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateUnion(ShapedQueryExpression source1, ShapedQueryExpression source2)
    {
        throw new NotImplementedException();
    }

    protected override ShapedQueryExpression? TranslateWhere(ShapedQueryExpression source, LambdaExpression predicate)
    {
        throw new NotImplementedException();
    }
}
