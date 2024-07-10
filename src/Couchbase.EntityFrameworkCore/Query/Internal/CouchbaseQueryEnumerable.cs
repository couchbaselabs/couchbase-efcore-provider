using System.Collections;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>
{
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly RelationalCommandCache _relationalCommandCache;
    private readonly IClusterProvider _clusterProvider;
   
    public CouchbaseQueryEnumerable(
        RelationalQueryContext relationalQueryContext, 
        RelationalCommandCache relationalCommandCache,
        IClusterProvider clusterProvider)
    {
        _clusterProvider = clusterProvider;
        _relationalQueryContext = relationalQueryContext;
        _relationalCommandCache = relationalCommandCache;
    }
    
    public IEnumerator<T> GetEnumerator()
    {
        var queryOptions = new QueryOptions();
        foreach (var parameter in _relationalQueryContext.ParameterValues)
        {
            queryOptions.Parameter(parameter.Key, parameter.Value);
        }
        var command = _relationalCommandCache.RentAndPopulateRelationalCommand(_relationalQueryContext);
        var cluster = _clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        var result = cluster.QueryAsync<T>(command.CommandText, queryOptions).GetAwaiter().GetResult();
        return result.ToEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
    {
        var queryOptions = new QueryOptions();
        foreach (var parameter in _relationalQueryContext.ParameterValues)
        {
            queryOptions.Parameter(parameter.Key, parameter.Value);
        }
        var command = _relationalCommandCache.RentAndPopulateRelationalCommand(_relationalQueryContext);
        var cluster = _clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        var result = cluster.QueryAsync<T>(command.CommandText, queryOptions).GetAwaiter().GetResult();
        return result.GetAsyncEnumerator(cancellationToken);
    }
}