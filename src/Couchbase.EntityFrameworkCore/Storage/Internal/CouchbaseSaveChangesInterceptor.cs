using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Interceptor that defers AcceptAllChanges when a Couchbase transaction is active.
/// Entity states are restored after SaveChanges and only accepted when the transaction commits successfully.
/// 
/// This interceptor uses a ConditionalWeakTable to store per-DbContext state, making it safe to share
/// a single interceptor instance across multiple DbContext instances (as happens with cached DbContextOptions).
/// </summary>
public class CouchbaseSaveChangesInterceptor : SaveChangesInterceptor
{
    // Per-DbContext state, keyed by the DbContext instance.
    // ConditionalWeakTable automatically removes entries when the DbContext is garbage collected.
    private static readonly ConditionalWeakTable<DbContext, ContextTrackingState> _contextStates = new();

    /// <summary>
    /// Signals that a transaction has started and entity states should be preserved for the given context.
    /// </summary>
    internal static void BeginTracking(DbContext context)
    {
        var state = _contextStates.GetOrCreateValue(context);
        state.IsTransactionActive = true;
        state.TrackedEntities.Clear();
    }

    /// <summary>
    /// Signals that a transaction has ended and tracking should stop for the given context.
    /// </summary>
    internal static void EndTracking(DbContext context)
    {
        if (_contextStates.TryGetValue(context, out var state))
        {
            state.IsTransactionActive = false;
            state.TrackedEntities.Clear();
        }
    }

    /// <summary>
    /// Accepts all tracked changes after a successful commit for the given context.
    /// </summary>
    internal static void AcceptTrackedChanges(DbContext context)
    {
        if (!_contextStates.TryGetValue(context, out var state))
        {
            return;
        }

        foreach (var tracked in state.TrackedEntities)
        {
            var entry = context.Entry(tracked.Entity);
            
            if (tracked.OriginalState == EntityState.Deleted)
            {
                entry.State = EntityState.Detached;
            }
            else
            {
                entry.State = EntityState.Unchanged;
            }
        }
        state.TrackedEntities.Clear();
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && IsTransactionActive(eventData.Context))
        {
            CaptureEntityStates(eventData.Context);
        }
        
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context != null && IsTransactionActive(eventData.Context))
        {
            CaptureEntityStates(eventData.Context);
        }
        
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && IsTransactionActive(eventData.Context))
        {
            RestoreEntityStates(eventData.Context);
        }
        
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        if (eventData.Context != null && IsTransactionActive(eventData.Context))
        {
            RestoreEntityStates(eventData.Context);
        }
        
        return base.SavedChanges(eventData, result);
    }

    private static bool IsTransactionActive(DbContext context)
    {
        return _contextStates.TryGetValue(context, out var state) && state.IsTransactionActive;
    }

    private static void CaptureEntityStates(DbContext context)
    {
        var state = _contextStates.GetOrCreateValue(context);
        
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var tracked = new TrackedEntityState
                {
                    Entity = entry.Entity,
                    OriginalState = entry.State,
                    OriginalValues = entry.State == EntityState.Modified
                        ? CaptureOriginalValues(entry)
                        : null
                };
                state.TrackedEntities.Add(tracked);
            }
        }
    }

    private static void RestoreEntityStates(DbContext context)
    {
        if (!_contextStates.TryGetValue(context, out var state))
        {
            return;
        }

        foreach (var tracked in state.TrackedEntities)
        {
            var entry = context.Entry(tracked.Entity);

            switch (tracked.OriginalState)
            {
                case EntityState.Added:
                    entry.State = EntityState.Added;
                    break;

                case EntityState.Modified:
                    entry.State = EntityState.Modified;
                    if (tracked.OriginalValues != null)
                    {
                        foreach (var (propertyName, value) in tracked.OriginalValues)
                        {
                            entry.OriginalValues[propertyName] = value;
                        }
                    }
                    break;

                case EntityState.Deleted:
                    if (entry.State == EntityState.Detached)
                    {
                        context.Attach(tracked.Entity);
                    }
                    entry.State = EntityState.Deleted;
                    break;
            }
        }
    }

    private static Dictionary<string, object?> CaptureOriginalValues(EntityEntry entry)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.OriginalValues.Properties)
        {
            values[property.Name] = entry.OriginalValues[property];
        }
        return values;
    }

    /// <summary>
    /// Per-DbContext tracking state.
    /// </summary>
    private class ContextTrackingState
    {
        public bool IsTransactionActive { get; set; }
        public List<TrackedEntityState> TrackedEntities { get; } = new();
    }

    internal class TrackedEntityState
    {
        public required object Entity { get; init; }
        public required EntityState OriginalState { get; init; }
        public Dictionary<string, object?>? OriginalValues { get; init; }
    }
}
