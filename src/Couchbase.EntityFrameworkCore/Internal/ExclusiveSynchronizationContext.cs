using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Couchbase.EntityFrameworkCore.Internal;

// Note: Sealing this class can help JIT make non-virtual method calls and inlined method calls for virtual methods
internal sealed class ExclusiveSynchronizationContext(
    SynchronizationContext? parentSynchronizationContext)
    : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<WorkItem> _items = [];

    public SynchronizationContext? ParentSynchronizationContext => parentSynchronizationContext;

    public override void Send(SendOrPostCallback d, object? state)
    {
        throw new NotSupportedException("We cannot send to our same thread");
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(d);
#else
        if (d is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(d));
            return; // unreachable, but helps static analysis
        }
#endif

        try
        {
            _items.Add(new WorkItem(d, state));
            return;
        }
        catch (InvalidOperationException)
        {
            // This indicates the items cannot be added because the main loop
            // is complete. We can't do a boolean check on IsAddingComplete because
            // it will throw an ObjectDisposedException.
        }

        // The collection is closed to new items because we got done with the main task.
        // Instead, we post any remaining work to the parent SynchronizationContext.
        // This can occur if the main task starts additional work which isn't completed
        // before the main task completes.

        ExecuteOnParent(d, state);
    }

    private void EndMessageLoop()
    {
        // This method is only called when _items can still accept messages
        Debug.Assert(!_items.IsAddingCompleted);

        // We could post this onto the queue to be processed, but that's unnecessary because
        // we're registered via OnCompleted on the awaiter. This will already marshal us onto
        // this synchronization context.
        //
        // If we're already on this synchronization context when the main task completes,
        // this continuation will be executed inline rather than queued in most cases.

        _items.CompleteAdding();
    }

    public void Run<TAwaiter>(TAwaiter awaiter)
        where TAwaiter : struct, ICriticalNotifyCompletion
    {
        // Register a callback to run when the original awaiter is completed
        // We use UnsafeOnCompleted so it doesn't flow ExecutionContext
        awaiter.UnsafeOnCompleted(EndMessageLoop);

        while (!_items.IsCompleted)
        {
            var (callback, state) = _items.Take();

            callback(state);
        }
    }

    // Processes any remaining continuations in the queue
    public void RunAlreadyComplete()
    {
        EndMessageLoop();

        while (!_items.IsCompleted)
        {
            var (callback, state) = _items.Take();

            ExecuteOnParent(callback, state);
        }
    }

    // Executes a work item on the parent SynchronizationContext or on the thread pool if there is not one
    private void ExecuteOnParent(SendOrPostCallback callback, object? state)
    {
        if (ParentSynchronizationContext != null)
        {
            ParentSynchronizationContext.Post(callback, state);
        }
        else
        {
            // There is no parent sync context, so use the default behavior from the default
            // SynchronizationContext and post to the thread pool.

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            ThreadPool.QueueUserWorkItem(static s => s.Callback(s.State), new WorkItem(callback, state), preferLocal: false);
#else
                ThreadPool.QueueUserWorkItem(static s =>
                {
                    var (callback, state) = (WorkItem)s;
                    callback(state);
                }, new WorkItem(callback, state));
#endif
        }
    }

    public override SynchronizationContext CreateCopy()
    {
        return this;
    }

    public void Dispose()
    {
        _items.Dispose();
    }
}

