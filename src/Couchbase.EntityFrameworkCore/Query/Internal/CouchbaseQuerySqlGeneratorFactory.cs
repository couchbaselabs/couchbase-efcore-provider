using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly INamedBucketProvider _namedBucketProvider;
    private readonly INamedCollectionProvider _namedCollectionProvider;

    public CouchbaseQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies, INamedBucketProvider namedBucketProvider, INamedCollectionProvider namedCollectionProvider)
    {
        _dependencies = dependencies;
        _namedBucketProvider = namedBucketProvider;
        _namedCollectionProvider = namedCollectionProvider;
    }
    
    public QuerySqlGenerator Create()
    {
        return new CouchbaseQuerySqlGenerator(_dependencies, _namedBucketProvider, _namedCollectionProvider);
    }
}