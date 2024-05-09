using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryContextFactory : IQueryContextFactory
{
    private readonly ICouchbaseClientWrapper _couchbaseClient;

    public CouchbaseQueryContextFactory(QueryContextDependencies contextDependencies,
        ICouchbaseClientWrapper couchbaseClient)
    {
        _couchbaseClient = couchbaseClient;
        Dependencies = contextDependencies;
    }
    
    protected virtual QueryContextDependencies Dependencies { get; }
    public QueryContext Create() => new CouchbaseQueryContext(Dependencies, _couchbaseClient);
}