# Spec: OwnsMany State Manager Tracking & Owner Propagation

**Status:** Complete (implementation in rdr-phase4; write-path integration tests pending
live-server verification)

---

## Background

`PopulateCollectionNavigations` in `CouchbaseQueryEnumerable.cs` materializes owned
collection items from an embedded JSON column. For change tracking to work correctly,
each item must be registered with EF Core's state manager so that subsequent additions,
removals, and scalar mutations are detected by AutoDetectChanges and reflected in the
`IUpdateEntry` list passed to `SaveChangesAsync`.

A secondary problem exists for collection-reference replacement: when the user writes
`customer.ContactMethods = [ ... ]`, no individual owned item state changes — EF Core
sees no entries to save and skips the root entity entirely.

---

## Approach: Three cooperating mechanisms

### 1. State manager registration via `forMaterialization: true`

`PopulateCollectionNavigations` adds each owned item to the collection via EF Core's
`IClrCollectionAccessor.Add(owner, item, forMaterialization: true)`. The
`forMaterialization: true` flag routes through EF Core's collection accessor
infrastructure, which:

- Creates an `InternalEntityEntry` for the item and registers it with the state manager.
- Sets its initial state to `Unchanged`.
- Takes a property snapshot so that scalar mutations are detectable by AutoDetectChanges.

With items registered this way, the following mutations are all tracked automatically:

| Mutation | How EF Core detects it |
|---|---|
| `collection.Add(newItem)` | AutoDetectChanges finds the item in the collection but not in the snapshot → `Added` |
| `collection.Remove(item)` | AutoDetectChanges finds the item in the snapshot but not in the collection → `Deleted` |
| `item.Property = value` | Snapshot comparison detects property change → `Modified` |
| OwnsOne scalar mutation | Same snapshot mechanism → owned entity `Modified` |

### 2. Deferred second pass in `CouchbaseDatabaseWrapper.SaveChangesAsync`

`SaveChangesAsync` divides its work into two passes:

**First pass** — processes root (non-owned) entities in the normal way (Add/Update/Delete
via the key/value API). Deleted and written owners are recorded in `writtenRoots`.

**Second pass** — iterates `deferredOwnedEntries` (owned entries with state not
`Unchanged`/`Detached`):

1. Resolves the ownership relationship: `entityType.FindOwnership()`.
2. Extracts the FK values from the owned entry (these equal the owner's PK).
3. Calls `StateManager.TryGetEntry(ownership.PrincipalKey, fkValues)` — O(1) lookup of
   the owner's `InternalEntityEntry`, avoiding a linear `ChangeTracker.Entries()` scan.
4. Skips owners already in `writtenRoots` (already written or deleted in the first pass).
5. Calls `HydrateObjectFromEntity(ownerInternalEntry)` to build the full owner document
   (including the current state of all owned navigations) and upserts it.

This handles all four mutation cases in the table above.

### 3. Collection-reference replacement via `OwnedCollectionSnapshot` + interceptor

When the user replaces an entire collection reference (`customer.ContactMethods = []`),
EF Core's change tracker produces no owned-item entries — the old items were removed from
the collection externally, so they never get `Deleted` state, and the new items were never
added via a tracked accessor, so they never get `Added` state.

Detection and fix:

- **`OwnedCollectionSnapshot.OriginalRefs`** — a `ConditionalWeakTable<object, Dictionary<string, object?>>` that stores the original collection-object reference for each OwnsMany navigation on every freshly materialised entity. Populated in `CouchbaseQueryEnumerable.SnapshotCollectionRefs` after `PopulateCollectionNavigations` runs.

- **`CouchbaseSaveChangesInterceptor.MarkOwnersWithReplacedCollections`** — fires just
  before `SaveChanges`. Iterates tracked root entities; for each OwnsMany navigation,
  compares the current property value against the stored reference. If they differ (i.e.
  the user replaced the collection), marks the owner as `Modified`. This forces EF Core to
  include the owner in the `entries` list passed to `SaveChangesAsync`, and the main loop
  writes the updated document (with the new collection contents from
  `HydrateObjectFromEntity`).

- **`CouchbaseSaveChangesInterceptor.RefreshOwnedCollectionSnapshots`** — fires after a
  successful save, updating `OriginalRefs` to the current collection references so
  subsequent saves do not see a stale mismatch.

---

## Why this differs from the original spec

The previous version of this spec proposed manually calling
`IStateManager.GetOrCreateEntry(entity, entityType)`, `SetEntityState(Unchanged,
acceptChanges: true)`, and `SetRelationshipSnapshotValue(nav, list)` from within
`PopulateCollectionNavigations`. That approach would have required accessing internal
EF Core APIs (`#pragma warning disable EF1001`) and replicating logic that
`IClrCollectionAccessor.Add(owner, item, forMaterialization: true)` already performs.

The `forMaterialization: true` flag is the intended EF Core API for exactly this purpose
and handles state manager registration, snapshot creation, and navigation fixup
transparently. The separate `OwnedCollectionSnapshot` mechanism covers the
collection-reference-replacement edge case that the original spec did not anticipate.

---

## Integration tests

All tests are in `OwnedTypeTests.cs`.

### Read path (all passing)
- `OwnsOne_InlineAddress_IsPopulated`
- `OwnsMany_EmbeddedContactMethods_ArePopulated`
- `OwnsMany_SingleItem_IsPopulated`
- `Customers_AllHaveAddresses`
- `OwnsOne_FilterBy_OwnedProperty_ReturnsMatchingCustomer`
- `OwnsMany_ToListAsync_AllCustomersCollectionsPopulated`
- `OwnsMany_AsNoTracking_IsPopulated`
- `OwnsMany_ItemOrderIsPreserved`
- `OwnsMany_EmptyCollectionDocument_ReadsAsEmpty`

### Write path (mechanisms 2 and 3 above; pending live-server verification)

| Test | Mechanism | Mutation type |
|---|---|---|
| `OwnsOne_Update_RoundTrips` | Deferred second pass | OwnsOne scalar |
| `OwnsOne_NullScalars_RoundTrip` | Deferred second pass | OwnsOne null scalars |
| `OwnsMany_Update_RoundTrips` | Collection ref replacement | Full collection replacement |
| `OwnsMany_ClearCollection_RoundTrips` | Collection ref replacement | Empty list assignment |
| `Customer_Add_WithOwnedTypes_RoundTrips` | Normal Add path | Root entity insert |
| `Customer_Delete_RemovesDocument` | Normal Delete path | Root entity delete |
| `OwnsMany_AddSingleItem_RoundTrips` | Deferred second pass | `.Add()` on existing collection |
| `OwnsMany_RemoveSingleItem_RoundTrips` | Deferred second pass | `.Remove()` from collection |
| `OwnsMany_MutateItemProperty_RoundTrips` | Deferred second pass | In-place scalar mutation |
