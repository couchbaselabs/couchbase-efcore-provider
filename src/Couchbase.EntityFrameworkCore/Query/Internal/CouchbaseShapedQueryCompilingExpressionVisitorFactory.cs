using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitorFactory : IShapedQueryCompilingExpressionVisitorFactory
{
    public readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies;

    public CouchbaseShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies)
    {
        _dependencies = dependencies;
    }
    
    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
    {
        return new CouchbaseShapedQueryCompilingExpressionVisitor(_dependencies, queryCompilationContext);
    }
}