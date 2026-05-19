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

                // Get the value generator from the selector
                var generator = selector.Select(property, entityType);

                if (generator == null)
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

    // For each tracked Unchanged/Detached root entity whose OwnsMany collection reference
    // was replaced (e.g. customer.ContactMethods = []), mark it as Modified so EF Core
    // includes it in GetEntriesToSave and our SaveChangesAsync is not skipped entirely.
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

                foreach (var nav in entry.Metadata.GetNavigations()
                             .Where(n => n.TargetEntityType.IsOwned() && n.IsCollection && n.PropertyInfo != null))
                {
                    if (!origRefs.TryGetValue(nav.Name, out var origRef)) continue;
                    if (ReferenceEquals(origRef, nav.PropertyInfo!.GetValue(owner))) continue;

                    entry.State = EntityState.Modified;
                    break;
                }
            }
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
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
