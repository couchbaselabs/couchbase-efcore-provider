namespace Couchbase.EntityFrameworkCore.Internal;

/// <summary>
/// Runs an asynchronous operation and blocks on it, for a synchronous API. Every blocking path in the
/// provider goes through here, so none of them can skip the
/// <c>CouchbaseEventId.SyncNotSupported</c> check.
/// </summary>
internal static class SyncIo
{
    /// <summary>Calls <paramref name="check"/>, then blocks on <paramref name="operation"/>.</summary>
    /// <param name="check">
    /// Logs <c>SyncNotSupported</c>, which throws unless the context opted in to synchronous I/O.
    /// Null when there is no context to ask (an ADO.NET connection created outside a context); the
    /// call is then allowed.
    /// </param>
    public static void Run<TState>(Action? check, Func<TState, Task> operation, TState state)
    {
        check?.Invoke();
        AsyncHelper.RunSync(operation, state);
    }

    /// <inheritdoc cref="Run{TState}(Action?, Func{TState, Task}, TState)"/>
    public static TResult Run<TState, TResult>(Action? check, Func<TState, Task<TResult>> operation, TState state)
    {
        check?.Invoke();
        return AsyncHelper.RunSync(operation, state);
    }

    /// <inheritdoc cref="Run{TState}(Action?, Func{TState, Task}, TState)"/>
    public static TResult Run<TState, TResult>(Action? check, Func<TState, ValueTask<TResult>> operation, TState state)
    {
        check?.Invoke();
        return AsyncHelper.RunSync(operation, state);
    }
}
