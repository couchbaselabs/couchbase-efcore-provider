using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
{
    public CouchbaseShapedQueryCompilingExpressionVisitor(ShapedQueryCompilingExpressionVisitorDependencies dependencies, QueryCompilationContext queryCompilationContext) 
        : base(dependencies, queryCompilationContext)
    {
    }

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        throw new NotImplementedException();
    }
}