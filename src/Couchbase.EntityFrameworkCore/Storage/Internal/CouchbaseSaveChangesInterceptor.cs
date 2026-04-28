using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Interceptor that defers AcceptAllChanges when a Couchbase transaction is active.
/// Entity states are restored after SaveChanges and only accepted when the transaction commits successfully.
/// </summary>
public class CouchbaseSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly List<TrackedEntityState> _trackedEntities = new();
    private bool _isTransactionActive;

    /// <summary>
    /// Signals that a transaction has started and entity states should be preserved.
    /// </summary>
    internal void BeginTracking()
    {
        _isTransactionActive = true;
        _trackedEntities.Clear();
    }

    /// <summary>
    /// Signals that a transaction has ended and tracking should stop.
    /// </summary>
    internal void EndTracking()
    {
        _isTransactionActive = false;
        _trackedEntities.Clear();
    }

    /// <summary>
    /// Accepts all tracked changes after a successful commit.
    /// </summary>
    internal void AcceptTrackedChanges(DbContext context)
    {
        foreach (var tracked in _trackedEntities)
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
        _trackedEntities.Clear();
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_isTransactionActive && eventData.Context != null)
        {
            CaptureEntityStates(eventData.Context);
        }
        
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (_isTransactionActive && eventData.Context != null)
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
        if (_isTransactionActive && eventData.Context != null)
        {
            RestoreEntityStates(eventData.Context);
        }
        
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        if (_isTransactionActive && eventData.Context != null)
        {
            RestoreEntityStates(eventData.Context);
        }
        
        return base.SavedChanges(eventData, result);
    }

    private void CaptureEntityStates(DbContext context)
    {
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
                _trackedEntities.Add(tracked);
            }
        }
    }

    private void RestoreEntityStates(DbContext context)
    {
        foreach (var tracked in _trackedEntities)
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

    internal class TrackedEntityState
    {
        public required object Entity { get; init; }
        public required EntityState OriginalState { get; init; }
        public Dictionary<string, object?>? OriginalValues { get; init; }
    }
}
