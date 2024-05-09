using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryCompilationContext : QueryCompilationContext
{
    public CouchbaseQueryCompilationContext(QueryCompilationContextDependencies dependencies, bool async) 
        : base(dependencies, async)
    {
    }
}