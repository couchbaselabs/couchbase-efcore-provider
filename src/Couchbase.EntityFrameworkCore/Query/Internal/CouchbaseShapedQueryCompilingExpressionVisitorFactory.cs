using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitorFactory : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies;
    private readonly RelationalShapedQueryCompilingExpressionVisitorDependencies _relationalDependencies;
    private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;

    public CouchbaseShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies, 
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        IQuerySqlGeneratorFactory querySqlGeneratorFactory,
        IServiceProvider clusterProvider, 
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _dependencies = dependencies;
        _relationalDependencies = relationalDependencies;
        _querySqlGeneratorFactory = querySqlGeneratorFactory;
        _serviceProvider = clusterProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }
    
    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
    {
        //the QuerySqlGenerator may be cacheable - if so make a field and call create in ctor above
        return new CouchbaseShapedQueryCompilingExpressionVisitor(
            _dependencies, 
            _relationalDependencies, 
            queryCompilationContext, 
            _querySqlGeneratorFactory.Create(),
            _serviceProvider,
            _couchbaseDbContextOptionsBuilder);
    }
}