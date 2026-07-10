namespace Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;

/// <summary>
/// Shared polling helpers for tests that must tolerate eventual consistency (e.g. a KV read
/// immediately after a transaction commit, or an N1QL query before an index catches up).
/// Kept as a single implementation so timeout/interval semantics can't drift between the
/// transaction-related test classes that need this.
/// </summary>
public static class PollingHelper
{
    /// <summary>
    /// Repeatedly invokes <paramref name="query"/> until <paramref name="condition"/> is met or
    /// <paramref name="timeout"/> elapses, returning the final result either way (letting a
    /// caller's assertion fail with the actual value rather than swallowing the timeout).
    /// </summary>
    public static async Task<T?> PollForResultAsync<T>(
        Func<Task<T?>> query,
        Func<T?, bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await query();
            if (condition(result))
            {
                return result;
            }
            await Task.Delay(interval);
        }

        // Return final result even if condition not met (let assertion fail with actual value).
        return await query();
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it returns true, throwing <see cref="TimeoutException"/>
    /// if it never does within <paramref name="timeout"/> — a test helper that silently returned on
    /// timeout would let a caller's test continue (and potentially pass) despite the condition never
    /// being satisfied.
    /// </summary>
    public static async Task PollUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(interval);
        }

        throw new TimeoutException($"Condition was not met within {timeout}.");
    }
}
