# Spec: Hook OwnsMany Items into EF Core State Manager

## Background

`PopulateCollectionNavigations` in `CouchbaseQueryEnumerable.cs` materializes owned
collection items by calling `Activator.CreateInstance`, setting properties via
reflection, and assigning the resulting `List<T>` directly to the navigation property.
The EF Core shaper has already tracked the root entity (e.g. `Customer`) via the state
manager — but the owned items injected afterward are invisible to it.

## What EF Core does normally

In the standard relational provider, owned collection members arrive as JOIN rows and are
materialized inside the compiled shaper function. The shaper calls
`IStateManager.GetOrCreateEntry(entity, entityType)` for each owned item and sets its
state to `Unchanged`, then links it to the owner's `InternalEntityEntry` via the
navigation fix-up infrastructure. The result is a fully tracked object graph.

## What needs to change

### 1. Change `PopulateCollectionNavigations` from `private static` to an instance method

It currently takes no context. It needs access to:

- `_relationalQueryContext.StateManager` — to create tracking entries
- `_standAloneStateManager` — to know whether tracking is active

### 2. Resolve the owner's `InternalEntityEntry`

After the shaper yields the root entity, retrieve its entry:

```csharp
var ownerEntry = _relationalQueryContext.StateManager.TryGetEntry(entity, _ownerEntityType);
```

If `ownerEntry` is null (no-tracking query), skip state manager registration entirely
and fall through to the existing assignment path — no-tracking behavior is unchanged.

### 3. For each owned item, create a tracked entry and set it `Unchanged`

```csharp
var ownedEntry = _relationalQueryContext.StateManager
    .GetOrCreateEntry(ownedEntity, nav.TargetEntityType);
ownedEntry.SetEntityState(EntityState.Unchanged, acceptChanges: true);
```

### 4. Run EF Core's navigation fix-up

After all items are added, trigger fix-up so the state manager's internal navigation
graph is consistent:

```csharp
ownerEntry.SetRelationshipSnapshotValue(nav, list);
```

This is closer to what the compiled shaper does and avoids a full `DetectChanges` scan.
Alternatively, call `_relationalQueryContext.StateManager.Context.ChangeTracker.DetectChanges()`
but that is more expensive.

### 5. No-tracking path

When `_standAloneStateManager` is `true` (no-tracking query), `StateManager.TryGetEntry`
will return null. The method should detect this and skip all state manager calls, keeping
the existing direct-assignment path as the no-tracking fast path. Both paths end with the
same `nav.PropertyInfo.SetValue(entity, list)` call.

## Callsite change

`GetAsyncEnumerator` calls `PopulateCollectionNavigations` in three places — all three
need to pass the root entity to the instance method so the owner entry can be resolved.

## Risk surface

- `IStateManager.GetOrCreateEntry(object, IEntityType)` is internal EF Core API
  (`Microsoft.EntityFrameworkCore.ChangeTracking.Internal`). It is already used indirectly
  via `InitializeStateManager` in this file, so the dependency is not new — but it may
  require `#pragma warning disable EF1001` suppressions.
- Calling this on a no-tracking context will throw; the null-check on `TryGetEntry` is
  the guard.
- Owned entity primary keys in EF Core include a generated shadow property (e.g.
  `__synthesizedOrdinal`). When creating entries manually, those shadow properties must be
  set to valid values (typically the zero-based index in the list) or EF Core's key
  uniqueness check will fail for collections with more than one item.
