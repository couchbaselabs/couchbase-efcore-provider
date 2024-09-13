using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly INamedBucketProvider _namedBucketProvider;

    public CouchbaseQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies, INamedBucketProvider namedBucketProvider)
    {
        _dependencies = dependencies;
        _namedBucketProvider = namedBucketProvider;
    }
    
    public QuerySqlGenerator Create()
    {
        return new CouchbaseQuerySqlGenerator(_dependencies, _namedBucketProvider);
    }
}