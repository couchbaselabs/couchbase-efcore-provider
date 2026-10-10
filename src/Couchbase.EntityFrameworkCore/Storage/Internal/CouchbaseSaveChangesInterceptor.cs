using System.Collections;
using System.Runtime.CompilerServices;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Interceptor that records, per <c>SaveChanges</c> call, the entity states that were saved while a
/// Couchbase transaction is active.
/// </summary>
/// <remarks>
/// Within the transaction each save leaves its entities in the state EF Core gives them (accepted),
/// so a later <c>SaveChanges</c> only sends new changes instead of re-queuing earlier work. The
/// recorded batches are used to put entities back to their pending state when that work is not
/// persisted — a failed commit, a rollback, a transaction disposed without committing, or a
/// rollback to a savepoint — so the change tracker never claims something was saved that wasn't.
///
/// This interceptor uses a ConditionalWeakTable to store per-DbContext state, making it safe to share
/// a single interceptor instance across multiple DbContext instances (as happens with cached DbContextOptions).
/// </remarks>
public class CouchbaseSaveChangesInterceptor : SaveChangesInterceptor
{
    // Per-DbContext state, keyed by the DbContext instance.
    // ConditionalWeakTable automatically removes entries when the DbContext is garbage collected.
    private static readonly ConditionalWeakTable<DbContext, ContextTrackingState> _contextStates = new();

    /// <summary>
    /// Signals that a transaction has started and saves should be recorded for the given context.
    /// </summary>
    internal static void BeginTracking(DbContext context)
    {
        var state = _contextStates.GetOrCreateValue(context);
        state.IsTransactionActive = true;
        state.Batches.Clear();
        state.PendingBatch = null;
    }

    /// <summary>
    /// Signals that a transaction has ended and tracking should stop for the given context.
    /// </summary>
    internal static void EndTracking(DbContext context)
    {
        if (_contextStates.TryGetValue(context, out var state))
        {
            state.IsTransactionActive = false;
            state.Batches.Clear();
            state.PendingBatch = null;
        }
    }

    /// <summary>
    /// The number of saves recorded so far in the active transaction. A savepoint remembers this so
    /// rolling back to it can restore exactly the saves made after it.
    /// </summary>
    internal static int BatchCount(DbContext context)
        => _contextStates.TryGetValue(context, out var state) ? state.Batches.Count : 0;

    /// <summary>
    /// The transaction committed: the entities were already accepted as each save completed, so
    /// there is nothing to restore — just stop remembering them.
    /// </summary>
    internal static void AcceptTrackedChanges(DbContext context)
    {
        if (_contextStates.TryGetValue(context, out var state))
        {
            state.Batches.Clear();
        }
    }

    /// <summary>
    /// Puts the entities of every save from <paramref name="firstBatchToRestore"/> onwards back to
    /// the state they had before that save (Added/Modified/Deleted — "pending"), newest save first
    /// so an entity saved more than once ends up in the state it had before its earliest restored
    /// save. Used when that work was not persisted, leaving the tracker as it would be after a
    /// failed <c>SaveChanges</c>.
    /// </summary>
    internal static void RestoreTrackedChanges(DbContext context, int firstBatchToRestore = 0)
    {
        if (!_contextStates.TryGetValue(context, out var state))
        {
            return;
        }

        for (var i = state.Batches.Count - 1; i >= firstBatchToRestore; i--)
        {
            RestoreEntityStates(context, state.Batches[i]);
            state.Batches.RemoveAt(i);
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {

        if (eventData.Context != null)
        {
            await GenerateSequenceValuesAsync(eventData.Context, cancellationToken);
            MarkOwnersWithReplacedCollections(eventData.Context);

            if (IsTransactionActive(eventData.Context))
            {
                CaptureEntityStates(eventData.Context);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context != null)
        {
            GenerateSequenceValuesAsync(eventData.Context, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            MarkOwnersWithReplacedCollections(eventData.Context);

            if (IsTransactionActive(eventData.Context))
            {
                CaptureEntityStates(eventData.Context);
            }
        }

        return base.SavingChanges(eventData, result);
    }

    private static async ValueTask GenerateSequenceValuesAsync(DbContext context, CancellationToken cancellationToken)
    {
        var selector = context.GetService<IValueGeneratorSelector>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var entityType = entry.Metadata;

            foreach (var property in entityType.GetProperties())
            {
                // Check if this property uses a Couchbase sequence
                var sequenceName = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value as string;

                if (string.IsNullOrEmpty(sequenceName))
                {
                    continue;
                }

                // Check if the property needs a value generated
                // EF Core may have already assigned a temporary negative value for tracking purposes
                var propertyEntry = entry.Property(property.Name);

                // Skip if the value was explicitly set by the user (not temporary and not default)
                if (!propertyEntry.IsTemporary)
                {
                    var currentValue = propertyEntry.CurrentValue;
                    var clrType = property.ClrType;
                    var defaultValue = clrType.IsValueType ? Activator.CreateInstance(clrType) : null;

                    if (currentValue != null && !currentValue.Equals(defaultValue))
                    {
                        // Value was explicitly set by user, skip generation
                        continue;
                    }
                }

                // Get the value generator from the selector (throw if none — the documented
                // replacement for the obsolete Select).
                if (!selector.TrySelect(property, entityType, out var generator) || generator == null)
                {
                    throw new InvalidOperationException(
                        $"Could not create value generator for property '{property.Name}' on entity '{entityType.ClrType.Name}'. " +
                        $"Selector type: {selector?.GetType().FullName}");
                }

                // Generate the value
                var value = await generator.NextAsync(entry, cancellationToken);
                entry.Property(property.Name).CurrentValue = value;
            }
        }
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null)
        {
            if (IsTransactionActive(eventData.Context))
            {
                CompleteBatch(eventData.Context);
            }

            // Saved entities are accepted now (inside a transaction too) — refresh snapshots so
            // later saves don't see a stale mismatch from the just-written state.
            RefreshOwnedCollectionSnapshots(eventData.Context);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        if (eventData.Context != null)
        {
            if (IsTransactionActive(eventData.Context))
            {
                CompleteBatch(eventData.Context);
            }

            // Saved entities are accepted now (inside a transaction too) — refresh snapshots so
            // later saves don't see a stale mismatch from the just-written state.
            RefreshOwnedCollectionSnapshots(eventData.Context);
        }

        return base.SavedChanges(eventData, result);
    }

    // For each tracked Unchanged/Detached root entity whose OwnsMany collection has changed
    // (reference replaced, items added/removed, or item property mutated), mark it as Modified
    // so EF Core includes it in GetEntriesToSave and our SaveChangesAsync rewrites the document.
    //
    // GenerateSequenceValuesAsync (called just before this) already triggered DetectChanges,
    // so we disable AutoDetectChanges here to avoid a redundant second pass.
    private static void MarkOwnersWithReplacedCollections(DbContext context)
    {
        var autoDetect = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.Metadata.IsOwned()) continue;
                if (entry.State is not (EntityState.Unchanged or EntityState.Detached)) continue;

                var owner = entry.Entity;
                if (!OwnedCollectionSnapshot.OriginalRefs.TryGetValue(owner, out var origRefs)) continue;

                OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var origItems);

                foreach (var nav in entry.Metadata.GetNavigations()
                             .Where(n => n.TargetEntityType.IsOwned() && n.IsCollection && n.PropertyInfo != null))
                {
                    if (!origRefs.TryGetValue(nav.Name, out var origRef)) continue;

                    var current = nav.PropertyInfo!.GetValue(owner);

                    // Reference replaced (e.g. customer.ContactMethods = [])
                    if (!ReferenceEquals(origRef, current))
                    {
                        entry.State = EntityState.Modified;
                        break;
                    }

                    // Same reference — check item count and property values for in-place changes
                    // (.Add(), .Remove(), or scalar mutation on an existing item).
                    if (origItems != null
                        && origItems.TryGetValue(nav.Name, out var origItemSnapshots)
                        && HasCollectionChanged(current, nav, origItemSnapshots))
                    {
                        entry.State = EntityState.Modified;
                        break;
                    }
                }
            }
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    // Returns true when the current collection differs from the original item-value snapshot:
    // count changed (Add / Remove) or any scalar property value changed (mutation).
    private static bool HasCollectionChanged(
        object? current,
        INavigation nav,
        IReadOnlyList<Dictionary<string, object?>> origItemSnapshots)
    {
        if (current is not IEnumerable collection) return false;

        var itemProps = OwnedCollectionSnapshot.GetTrackedProperties(nav);

        var currentItems = new List<object>();
        foreach (var item in collection)
            if (item != null) currentItems.Add(item);

        if (currentItems.Count != origItemSnapshots.Count) return true;

        for (var i = 0; i < currentItems.Count; i++)
        {
            var origSnapshot = origItemSnapshots[i];
            foreach (var prop in itemProps)
            {
                var currentValue = prop.PropertyInfo?.GetValue(currentItems[i]);
                if (!origSnapshot.TryGetValue(prop.Name, out var origValue)) return true;
                if (!OwnedCollectionSnapshot.GetComparer(prop).Equals(currentValue, origValue)) return true;
            }
        }

        return false;
    }

    // After a successful save, update OwnedCollectionSnapshot with the current collection
    // reference AND current item property values so subsequent SaveChanges calls won't see
    // a stale mismatch from the just-written state.
    private static void RefreshOwnedCollectionSnapshots(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.IsOwned()) continue;
            if (entry.State != EntityState.Unchanged) continue;

            var owner = entry.Entity;
            if (!OwnedCollectionSnapshot.OriginalRefs.TryGetValue(owner, out var refs)) continue;

            var itemsTable = OwnedCollectionSnapshot.OriginalItems.GetOrCreateValue(owner);

            foreach (var nav in entry.Metadata.GetNavigations()
                         .Where(n => n.TargetEntityType.IsOwned() && n.IsCollection && n.PropertyInfo != null))
            {
                var current = nav.PropertyInfo!.GetValue(owner);
                refs[nav.Name] = current;

                // Refresh item-level snapshots so subsequent saves detect mutations correctly.
                if (current is IEnumerable collection)
                {
                    var itemProps = OwnedCollectionSnapshot.GetTrackedProperties(nav);
                    var snapshot = new List<Dictionary<string, object?>>();
                    foreach (var item in collection)
                    {
                        if (item == null) continue;
                        var propSnapshot = new Dictionary<string, object?>();
                        foreach (var prop in itemProps)
                        {
                            var raw = prop.PropertyInfo?.GetValue(item);
                            // Use EF Core's ValueComparer.Snapshot so mutable reference types
                            // (e.g. byte[]) are deep-copied. For immutable types (string, int, …)
                            // Snapshot is a no-op that returns the same reference.
                            propSnapshot[prop.Name] = raw is null ? null : OwnedCollectionSnapshot.GetComparer(prop).Snapshot(raw);
                        }
                        snapshot.Add(propSnapshot);
                    }
                    itemsTable[nav.Name] = snapshot;
                }
                else
                {
                    itemsTable[nav.Name] = [];
                }
            }
        }
    }

    private static bool IsTransactionActive(DbContext context)
    {
        return _contextStates.TryGetValue(context, out var state) && state.IsTransactionActive;
    }

    // Records what is about to be saved as a pending batch. It only becomes a recorded batch once
    // the save completes (CompleteBatch); a save that throws never reaches SavedChanges, and EF
    // leaves those entities pending itself.
    private static void CaptureEntityStates(DbContext context)
    {
        var state = _contextStates.GetOrCreateValue(context);
        var batch = new List<TrackedEntityState>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                batch.Add(new TrackedEntityState
                {
                    Entity = entry.Entity,
                    OriginalState = entry.State,
                    OriginalValues = entry.State == EntityState.Modified
                        ? CaptureOriginalValues(entry)
                        : null
                });
            }
        }

        state.PendingBatch = batch;
    }

    private static void CompleteBatch(DbContext context)
    {
        if (_contextStates.TryGetValue(context, out var state) && state.PendingBatch is { } batch)
        {
            state.PendingBatch = null;
            if (batch.Count > 0)
            {
                state.Batches.Add(batch);
            }
        }
    }

    private static void RestoreEntityStates(DbContext context, List<TrackedEntityState> batch)
    {
        foreach (var tracked in batch)
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

        // One entry per completed SaveChanges in the active transaction, oldest first.
        public List<List<TrackedEntityState>> Batches { get; } = new();

        // The SaveChanges currently in flight; becomes a Batch when it completes.
        public List<TrackedEntityState>? PendingBatch { get; set; }
    }

    internal class TrackedEntityState
    {
        public required object Entity { get; init; }
        public required EntityState OriginalState { get; init; }
        public Dictionary<string, object?>? OriginalValues { get; init; }
    }
}
