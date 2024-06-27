using System.Collections;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>
{
    private readonly IRelationalCommand _relationalCommand;
    private readonly IClusterProvider _clusterProvider;

    public CouchbaseQueryEnumerable(
        IRelationalCommand relationalCommand, IClusterProvider clusterProvider)
    {
        _relationalCommand = relationalCommand;
        _clusterProvider = clusterProvider;
    }
    
    public IEnumerator<T> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
    {
        var cluster = _clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        var result = cluster.QueryAsync<T>(_relationalCommand.CommandText).GetAwaiter().GetResult();
        return result.GetAsyncEnumerator(cancellationToken);
    }
}