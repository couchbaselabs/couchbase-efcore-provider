using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryContext : QueryContext
{
    public ICouchbaseClientWrapper CouchbaseClient { get; }

    public CouchbaseQueryContext(QueryContextDependencies dependencies, ICouchbaseClientWrapper couchbaseClient) : base(dependencies)
    {
        CouchbaseClient = couchbaseClient;
    }
}   