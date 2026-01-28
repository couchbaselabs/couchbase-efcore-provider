using Couchbase.Core.Retry;
using Couchbase.Query;

namespace Couchbase.EntityFrameworkCore.UnitTests.Fakes;

public class FakeQueryResult<T> : IQueryResult<T>
{
    private bool _disposed;
    private IEnumerable<T> _results = Enumerable.Empty<T>();
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(
        CancellationToken cancellationToken = new CancellationToken())
    {
        return _results.ToAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
    }

    public RetryReason RetryReason { get; set; }
    public IAsyncEnumerable<T> Rows { get; set; }
    public QueryMetaData? MetaData { get; set; }
    public List<Error> Errors { get; set; }
}

