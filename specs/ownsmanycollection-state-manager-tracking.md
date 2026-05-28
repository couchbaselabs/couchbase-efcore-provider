# Spec: OwnsMany State Manager Tracking & Owner Propagation

**Status:** Complete (implementation in rdr-phase4/rdr-phase5; all 21 integration tests verified
on live server)

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

## Approach: Four cooperating mechanisms

### 1. State manager registration via `forMaterialization: true`

`PopulateCollectionNavigations` adds each owned item to the collection via EF Core's
`IClrCollectionAccessor.Add(owner, item, forMaterialization: true)`. The
`forMaterialization: true` flag is a pure CLR-level add — it places the item in the
collection object but **does not** register it with EF Core's `IStateManager`. Items are
therefore not tracked individually.

This is sufficient for:
- Navigation fixup during materialisation
- Deduplication (the accessor clear before the add loop prevents double-population)

OwnsOne scalars **are** tracked individually because EF Core's shaper materialises them as
owned entity entries in the state manager automatically. In-place OwnsMany mutations
(.Add, .Remove, scalar mutation) require mechanism 4 below.

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
  successful save, updating both `OriginalRefs` and `OriginalItems` to the current state
  so subsequent saves do not see stale mismatches.

### 4. In-place OwnsMany mutation via content-snapshot

When the user mutates an existing collection **in place** — `collection.Add(item)`,
`collection.Remove(item)`, or `item.Property = value` — the collection object reference
does not change. Mechanism 3's `ReferenceEquals` check passes, and EF Core's change
tracker does not see any owned-item state changes (because items are not individually
tracked — see mechanism 1). Without additional detection, `SaveChangesAsync` would skip
the owner entirely and the mutation would be lost.

Detection and fix:

- **`OwnedCollectionSnapshot.OriginalItems`** — a
  `ConditionalWeakTable<object, Dictionary<string, IReadOnlyList<Dictionary<string, object?>>>>`
  that stores an ordered snapshot of per-item property values for every OwnsMany navigation
  on a just-materialised entity. Populated alongside `OriginalRefs` in
  `CouchbaseQueryEnumerable.SnapshotCollectionRefs`.

- **`CouchbaseSaveChangesInterceptor.HasCollectionChanged`** — compares the current
  collection to the stored snapshot. Returns `true` if:
  - Item count differs (`.Add()` or `.Remove()`).
  - Any item's property value differs from the snapshot (in-place scalar mutation).

- `MarkOwnersWithReplacedCollections` calls `HasCollectionChanged` when the reference
  comparison passes. A detected content change marks the owner as `Modified` so EF Core
  includes it in the entries list and the document is rewritten.

- **`RefreshOwnedCollectionSnapshots`** also updates `OriginalItems` after a successful
  save so the snapshot stays in sync with the committed state.

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

### Write path (mechanisms 2–4 above; all passing)

| Test | Mechanism | Mutation type |
|---|---|---|
| `OwnsOne_Update_RoundTrips` | 2 — Deferred second pass | OwnsOne scalar |
| `OwnsOne_NullScalars_RoundTrip` | 2 — Deferred second pass | OwnsOne null scalars |
| `OwnsMany_Update_RoundTrips` | 3 — Collection ref replacement | Full collection replacement |
| `OwnsMany_ClearCollection_RoundTrips` | 3 — Collection ref replacement | Empty list assignment |
| `Customer_Add_WithOwnedTypes_RoundTrips` | Normal Add path | Root entity insert |
| `Customer_Delete_RemovesDocument` | Normal Delete path | Root entity delete |
| `OwnsMany_AddSingleItem_RoundTrips` | 4 — Content-snapshot (count change) | `.Add()` on existing collection |
| `OwnsMany_RemoveSingleItem_RoundTrips` | 4 — Content-snapshot (count change) | `.Remove()` from collection |
| `OwnsMany_MutateItemProperty_RoundTrips` | 4 — Content-snapshot (value change) | In-place scalar mutation |
| `OwnsMany_MutateItemProperty_SaveTwice_SecondSaveIsNoOp` | 4 — Content-snapshot + refresh | No-op second save after snapshot refresh |
| `OwnsMany_AddItem_SaveTwice_SecondSaveIsNoOp` | 4 — Content-snapshot + refresh | No-op second save (count-change path) |
| `OwnsMany_AddAndMutate_BothChangesSaved` | 4 — Content-snapshot | Combined `.Add()` + in-place scalar mutation |
