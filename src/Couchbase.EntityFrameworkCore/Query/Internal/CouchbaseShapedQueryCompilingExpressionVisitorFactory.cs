using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitorFactory : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies;
    private readonly RelationalShapedQueryCompilingExpressionVisitorDependencies _relationalDependencies;
    private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly IBucketProvider _bucketProvider;

    public CouchbaseShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies, 
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        IQuerySqlGeneratorFactory querySqlGeneratorFactory,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _dependencies = dependencies;
        _relationalDependencies = relationalDependencies;
        _querySqlGeneratorFactory = querySqlGeneratorFactory;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _bucketProvider = bucketProvider;
    }

    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
    {
        //the QuerySqlGenerator may be cacheable - if so make a field and call create in ctor above
        return new CouchbaseShapedQueryCompilingExpressionVisitor(
            _dependencies,
            _relationalDependencies,
            queryCompilationContext,
            _querySqlGeneratorFactory.Create(),
            _bucketProvider,
            _couchbaseDbContextOptionsBuilder);
    }
}