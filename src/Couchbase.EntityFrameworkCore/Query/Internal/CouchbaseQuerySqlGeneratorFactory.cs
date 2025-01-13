using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;

    public CouchbaseQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies, ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _dependencies = dependencies;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }
    
    public QuerySqlGenerator Create()
    {
        return new CouchbaseQuerySqlGenerator(_dependencies, _couchbaseDbContextOptionsBuilder);
    }
}