using System.Runtime.CompilerServices;

namespace Couchbase.EntityFrameworkCore.Internal;

/// <summary>
/// Executes asynchronous tasks synchronously.
/// </summary>
/// <remarks>
/// DO NOT use this class unless absolutely necessary. Calling async code from sync code is an anti-pattern
/// in most cases. This class is provided to assist in gradual conversion from sync to async code.
/// </remarks>
public static class AsyncHelper
{
    [ThreadStatic]
    private static bool t_InRunSync;

    // ValueTask-based overloads include OverloadResolutionPriority(-1) so that C# 13 and later will prefer the Task-based
    // overloads by default when both are applicable. This occurs when passing an async lambda directly to the RunSync method
    // without an explicit return type, for example:
    //
    //     AsyncHelper.RunSync(async () =>
    //     {
    //         await DoSomething();
    //         await DoSomethingElse();
    //     });

    // If there is no SynchronizationContext or if the current SynchronizationContext is the default one, and if there
    // is no custom TaskScheduler, then it is safe to run the task directly without risk of deadlock. This reduces
    // overhead and improves the speed of continuations because we don't need to use the ExclusiveSynchronizationContext.
    // Also, this is particularly valuable in cases where the thread being blocked is a thread pool thread. Modern .NET
    // includes optimizations which reduce the risk of thread pool depletion and otherwise improves performance when
    // waiting on a Task from a thread pool thread, something the ExclusiveSynchronizationContext cannot replicate.
    private static bool IsDeadlockSafe(SynchronizationContext? currentSynchronizationContext) =>
        (currentSynchronizationContext is null || currentSynchronizationContext.GetType() == typeof(SynchronizationContext))
            && ReferenceEquals(TaskScheduler.Current, TaskScheduler.Default);

    /// <summary>
    /// Gets a value indicating whether the current operation is executing synchronously within a call to
    /// <see cref="RunSync(Func{Task})"/> or a similar overload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This will return <see langword="false"/> for nested operations that are running on the thread pool, such as
    /// if <c>ConfigureAwait(false)</c> has been used and the continuation has moved to the thread pool.
    /// </para>
    /// <para>
    /// If the calling thread has no synchronization context, this property will return <see langword="true"/> only
    /// for the initial synchronous portion of the operation before any awaits that yield to the thread pool.
    /// </para>
    /// </remarks>
    public static bool IsRunningSynchronously => t_InRunSync;

    /// <summary>
    /// If <see cref="IsRunningSynchronously"/> is <see langword="true"/>, gets the previously installed <see cref="SynchronizationContext"/>
    /// that was replaced. Otherwise, returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// If operating within multiple nested calls to <see cref="RunSync(Func{Task})"/> or similar overloads, this method
    /// recurses to the outermost context and returns the <see cref="SynchronizationContext"/> that was
    /// replaced by the first call to <see cref="RunSync(Func{Task})"/> in the call stack. Always returns <see langword="null"/>
    /// if the replaced context is the default <see cref="SynchronizationContext"/>.
    /// </remarks>
    public static SynchronizationContext? GetReplacedSynchronizationContext()
    {
        // This check isn't the same as the check for IsRunningSynchronously. When in the IsDeadlockSafe path
        // t_InRunSync could be true while SynchronizationContext.Current is unchanged. However, that will
        // only happen if there is no SynchronizationContext to begin with, in which case we want to return null anyway.

        var context = SynchronizationContext.Current;
        if (context is not ExclusiveSynchronizationContext exclusiveSynchronizationContext)
        {
            // Not running within RunSync
            return null;
        }

        while (true)
        {
            var parentContext = exclusiveSynchronizationContext.ParentSynchronizationContext;
            if (parentContext is ExclusiveSynchronizationContext parentExclusiveSynchronizationContext)
            {
                // Recurse to the outer context
                exclusiveSynchronizationContext = parentExclusiveSynchronizationContext;
            }
            else
            {
                // Return the parent context, but only if it's not the default SynchronizationContext.
                // This provides consistency with await behaviors, which revert the context to null after an
                // await if the context is the default one.
                return parentContext is not null && !ReferenceEquals(parentContext.GetType(), typeof(SynchronizationContext))
                    ? parentContext
                    : null;
            }
        }
    }

    /// <summary>
    /// Executes an async <see cref="Task"/> method with no return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="Task"/> method to execute.</param>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    public static void RunSync(Func<Task> task)
    {
        RunSync(static state => state.Invoke(), task);
    }

    /// <summary>
    /// Executes an async <see cref="Task"/> method with no return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="Task"/> method to execute.</param>
    /// <param name="state">State to pass to the method.</param>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    public static void RunSync<TState>(Func<TState, Task> task, TState state)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return; // unreachable, but helps static analysis
        }
#endif

        using var _ = EnterRunSync();

        var oldContext = SynchronizationContext.Current;

        if (IsDeadlockSafe(oldContext))
        {
            task(state).GetAwaiter().GetResult();
            return;
        }

        using var synch = new ExclusiveSynchronizationContext(oldContext);
        SynchronizationContext.SetSynchronizationContext(synch);
        try
        {
            var awaiter = task(state).GetAwaiter();

            if (!awaiter.IsCompleted)
            {
                synch.Run(awaiter);
            }
            else
            {
                synch.RunAlreadyComplete();
            }

            // Throw any exception returned by the task
            awaiter.GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    /// <summary>
    /// Executes an async <see cref="ValueTask"/> method which has a void return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="ValueTask"/> method to execute.</param>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    [OverloadResolutionPriority(-1)]
    public static void RunSync(Func<ValueTask> task)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return; // unreachable, but helps static analysis
        }
#endif

        RunSync(static state => state.Invoke(), task);
    }

    /// <summary>
    /// Executes an async <see cref="ValueTask"/> method which has a void return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="ValueTask"/> method to execute.</param>
    /// <param name="state">State to pass to the method.</param>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    [OverloadResolutionPriority(-1)]
    public static void RunSync<TState>(Func<TState, ValueTask> task, TState state)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return; // unreachable, but helps static analysis
        }
#endif

        using var _ = EnterRunSync();

        var oldContext = SynchronizationContext.Current;

        if (IsDeadlockSafe(oldContext))
        {
            task(state).AsTask().GetAwaiter().GetResult();
            return;
        }

        using var synch = new ExclusiveSynchronizationContext(oldContext);
        SynchronizationContext.SetSynchronizationContext(synch);
        try
        {
#pragma warning disable CA2012
            var awaiter = task(state).GetAwaiter();
#pragma warning restore CA2012

            if (!awaiter.IsCompleted)
            {
                synch.Run(awaiter);
            }
            else
            {
                synch.RunAlreadyComplete();
            }

            // Throw any exception returned by the task
            awaiter.GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    /// <summary>
    /// Executes an async <see cref="Task{T}"/> method which has a void return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="Task{T}"/> method to execute.</param>
    /// <returns>The asynchronous result.</returns>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    public static T RunSync<T>(Func<Task<T>> task)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return default!; // unreachable, but helps static analysis
        }
#endif

        return RunSync(static state => state.Invoke(), task);
    }

    /// <summary>
    /// Executes an async <see cref="Task{T}"/> method which has a void return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="Task{T}"/> method to execute.</param>
    /// <param name="state">State to pass to the method.</param>
    /// <returns>The asynchronous result.</returns>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    public static T RunSync<T, TState>(Func<TState, Task<T>> task, TState state)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return default!; // unreachable, but helps static analysis
        }
#endif

        using var _ = EnterRunSync();

        var oldContext = SynchronizationContext.Current;

        if (IsDeadlockSafe(oldContext))
        {
            return task(state).GetAwaiter().GetResult();
        }

        using var synch = new ExclusiveSynchronizationContext(oldContext);
        SynchronizationContext.SetSynchronizationContext(synch);
        try
        {
            var awaiter = task(state).GetAwaiter();

            if (!awaiter.IsCompleted)
            {
                synch.Run(awaiter);
            }
            else
            {
                synch.RunAlreadyComplete();
            }

            // Throw any exception returned by the task or return the result
            return awaiter.GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    /// <summary>
    /// Executes an async <see cref="ValueTask{T}"/> method which has a void return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="ValueTask{T}"/> method to execute.</param>
    /// <returns>The asynchronous result.</returns>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    [OverloadResolutionPriority(-1)]
    public static T RunSync<T>(Func<ValueTask<T>> task)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return default!; // unreachable, but helps static analysis
        }
#endif

        return RunSync(static state => state.Invoke(), task);
    }

    /// <summary>
    /// Executes an async <see cref="ValueTask{T}"/> method which has a void return value synchronously.
    /// </summary>
    /// <param name="task"><see cref="ValueTask{T}"/> method to execute.</param>
    /// <param name="state">State to pass to the method.</param>
    /// <returns>The asynchronous result.</returns>
    /// <remarks>
    /// DO NOT use this method unless absolutely necessary. Calling async code from sync code is an anti-pattern
    /// in most cases. This method is provided to assist in gradual conversion from sync to async code.
    /// </remarks>
    [OverloadResolutionPriority(-1)]
    public static T RunSync<T, TState>(Func<TState, ValueTask<T>> task, TState state)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(task);
#else
        if (task is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(task));
            return default!; // unreachable, but helps static analysis
        }
#endif

        using var _ = EnterRunSync();

        var oldContext = SynchronizationContext.Current;

        if (IsDeadlockSafe(oldContext))
        {
            return task(state).AsTask().GetAwaiter().GetResult();
        }

        using var synch = new ExclusiveSynchronizationContext(oldContext);
        SynchronizationContext.SetSynchronizationContext(synch);
        try
        {
#pragma warning disable CA2012
            var awaiter = task(state).GetAwaiter();
#pragma warning restore CA2012

            if (!awaiter.IsCompleted)
            {
                synch.Run(awaiter);
            }
            else
            {
                synch.RunAlreadyComplete();
            }

            // Throw any exception returned by the task or return the result
            return awaiter.GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    // Provides a lightweight mechanism for using statements to clean up the thread-static t_InRunSync flag.

    private static InRunSyncCleanup EnterRunSync()
    {
        var previousState = t_InRunSync;
        t_InRunSync = true;
        return new InRunSyncCleanup(previousState);
    }

    private readonly ref struct InRunSyncCleanup(bool previousState) : IDisposable
    {
        public void Dispose()
        {
            if (!previousState)
            {
                t_InRunSync = false;
            }
        }
    }
}
