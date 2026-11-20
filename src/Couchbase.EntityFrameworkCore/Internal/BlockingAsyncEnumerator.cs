namespace Couchbase.EntityFrameworkCore.Internal;

/// <summary>
/// A synchronous view of an asynchronous enumerator, for applications that opted in to synchronous
/// I/O (<c>CouchbaseEventId.SyncNotSupported</c> ignored or logged). Each step blocks on the async one.
/// </summary>
internal sealed class BlockingAsyncEnumerator<T>(IAsyncEnumerator<T> inner) : IEnumerator<T>
{
    public T Current => inner.Current;

    object? System.Collections.IEnumerator.Current => Current;

    public bool MoveNext()
        => AsyncHelper.RunSync(static e => e.MoveNextAsync().AsTask(), inner);

    public void Reset() => throw new NotSupportedException();

    public void Dispose()
        => AsyncHelper.RunSync(static e => e.DisposeAsync().AsTask(), inner);
}
