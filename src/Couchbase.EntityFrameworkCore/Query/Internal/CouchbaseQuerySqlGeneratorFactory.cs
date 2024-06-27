using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    public CouchbaseQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
    {
        _dependencies = dependencies;
    }
    
    public QuerySqlGenerator Create()
    {
        return new CouchbaseQuerySqlGenerator(_dependencies);
    }
}