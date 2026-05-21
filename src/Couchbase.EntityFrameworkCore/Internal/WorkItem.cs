namespace Couchbase.EntityFrameworkCore.Internal;

internal readonly struct WorkItem(SendOrPostCallback callback, object? state)
{
    public readonly SendOrPostCallback Callback = callback;
    public readonly object? State = state;

    public void Deconstruct(out SendOrPostCallback callback, out object? state)
    {
        callback = Callback;
        state = State;
    }
}
