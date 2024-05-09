using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryableMethodTranslatingExpressionVisitorFactory : IQueryableMethodTranslatingExpressionVisitorFactory
{
    private readonly QueryableMethodTranslatingExpressionVisitorDependencies _dependencies;

    public CouchbaseQueryableMethodTranslatingExpressionVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies)
    {
        _dependencies = dependencies;
    }
    
    public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext) =>
        new CouchbaseQueryableMethodTranslatingExpressionVisitor(_dependencies, queryCompilationContext,
            subquery: false);
}