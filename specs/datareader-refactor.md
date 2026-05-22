# CouchbaseDbDataReader Refactor

## Background

`CouchbaseDbDataReader<T>` is the ADO.NET `DbDataReader` bridge between the Couchbase SDK's
`IQueryResult<JsonElement>` and EF Core's compiled shapers. It was built incrementally alongside
the query pipeline and has accumulated complexity for code paths that never execute in the
provider. This document describes four sequential phases to simplify and optimize it.

The EF Core shaper is the sole consumer of this reader. It calls typed getters by projection
ordinal (e.g. `GetInt32(3)`, `GetString(7)`) for every column of every result row. The
projection aliases (`_columnNames`) are always supplied by `CouchbaseQueryEnumerable` from
`SelectExpression.Projection`, so no runtime schema discovery is ever needed.

---

## Phase 1 — Remove Dead Code Paths

**Goal:** Delete code that never executes. No behavioral change.

### Changes

- **Switch `CouchbaseCommand.ExecuteDbDataReaderAsync` from `QueryAsync<object>` to
  `QueryAsync<JsonElement>`** and change the returned reader type from
  `CouchbaseDbDataReader<object>` to `CouchbaseDbDataReader<JsonElement>`. This was the
  origin of the non-`JsonElement` path in the reader — the ADO.NET command path was
  always using `T=object` while `CouchbaseQueryEnumerable` correctly used `T=JsonElement`.
  Aligning both callers to `JsonElement` is a prerequisite for the dead-code removal below.

- **Remove the non-`JsonElement` reflection branch** in `InitializeFieldInfo` and `GetFieldValue`.
  The `else if (_currentRow != null)` path uses `Type.GetProperties()` and `PropertyInfo.GetValue`
  to handle rows that are not `JsonElement`. With the command change above, this branch is
  now unreachable on every code path.

- **Fix `ConvertJsonElement` for `Object` and `Array` kinds.** The prior implementation called
  `ExtractFirstValueFromObject` / `ExtractFirstValueFromArray`, which extracted the first property
  or element from a complex value. This heuristic was wrong for legitimate complex fields (owned
  types, byte arrays stored as arrays) and only existed to paper over early row-structure confusion.
  Replace with returning the `JsonElement` as-is for those kinds, consistent with how
  `GetFieldValue<T>` already handles them.

- Remove `ExtractFirstValueFromObject` and `ExtractFirstValueFromArray` once the above is done.

### Expected outcome

Approximately 50 lines removed. The remaining code reflects only what actually runs in the
provider. Five tests required updates: the `IsType<CouchbaseDbDataReader<object>>` assertions
in both `CouchbaseCommandTests` and `CouchbaseDbDataReaderTests`, and the unit-test mock setups
that targeted `QueryAsync<object>` for the reader path. The `MultipleRows_CanIterate` integration
test also needed its N1QL query corrected from an invalid subquery alias to `UNION ALL` form.

---

## Phase 2 — Eliminate the `_fieldOrdinals` Indirection

**Goal:** Shorten the `GetValue` hot path from five steps to two. Logically equivalent to current
behavior.

### The current chain (when `_columnNames` is active)

```
GetValue(ordinal)
  → _columnNames[ordinal]          // array lookup → alias
  → _fieldOrdinals[alias]          // dict lookup (OrdinalIgnoreCase) → json ordinal
  → _fieldNames[jsonOrdinal]       // array lookup → same name we started with
  → GetFieldValue(name)
      → je.TryGetProperty(name)    // linear scan of JsonElement properties
```

Steps 2 and 3 round-trip through an intermediate ordinal and arrive at the same string. They add
two allocations and two lookups without changing the result.

### Changes

- When `_columnNames` is set, replace the round-trip through steps 2–3 (dict → array → same name)
  with a two-step fast path:
  1. `_fieldOrdinals.TryGetValue(alias)` — O(1) OrdinalIgnoreCase lookup to retrieve the
     canonical JSON property name stored in `_fieldNames[jsonOrd]`.
  2. `je.TryGetProperty(canonicalName)` — exact (case-sensitive) property read on the live
     `JsonElement` row.
  `TryGetPropertyCI` is retained as a fallback for aliases not present in `_fieldOrdinals`
  (e.g. computed aliases injected after schema discovery). A missing field returns `DBNull.Value`.

- Retain the no-`_columnNames` fallback path unchanged so this phase is self-contained and does
  not require simultaneous changes to `CouchbaseQueryEnumerable`.

- Retain the null-slot fallback in `GetOrdinal` unchanged: the `EnsureFieldInfo()` call, the
  `_fieldOrdinals` lookup, the bounds check `(uint)jsonOrd < (uint)_columnNames.Length`, and the
  `_columnNames[jsonOrd] == null` guard must all stay. The bounds check prevents extra JSON fields
  beyond the projection width from being surfaced via `reader["name"]`, and the null-slot guard
  ensures non-null aliases that somehow bypass `_projectionOrdinals` are rejected rather than
  silently returned. Only the stale comment above the block needs updating.

### Expected outcome

`GetValue` hot path (non-null `_columnNames` slot): array lookup → `_fieldOrdinals` dict lookup
→ exact `TryGetProperty`. One dictionary lookup replaces two (dict + array); the O(m) linear scan
is eliminated for the common case. `TryGetPropertyCI` is the fallback for unknown aliases only.
Approximately 15–20 lines removed from `GetValue`; `GetOrdinal` is comment-only cleanup.

---

## Phase 3 — Remove Schema-Discovery Infrastructure

**Goal:** Delete the first-row peek, buffering, and field-metadata machinery. `_columnNames` is
always provided so this infrastructure is never needed.

### Changes

- Make `columnNames` a required constructor parameter. Add a guard that throws
  `ArgumentNullException` if null. Remove the optional overload that omits it.

- Delete the following fields and their associated logic:
  - `_bufferedRow`, `_hasBufferedRow` — first-row buffer
  - `_schemaInitialized` — initialization flag
  - `_fieldNames` — list of JSON property names from first row
  - `_fieldOrdinals` — name → position map built from first row

- Delete `EnsureFieldInfo` and `InitializeFieldInfo` entirely. These contain the only
  sync-over-async call in the reader (`MoveNextAsync().AsTask().GetAwaiter().GetResult()`).

- Simplify the properties that previously triggered schema discovery:
  - `HasRows` — cannot be known until `ReadAsync` is called; return `_hasRows ?? false`
    (populated on first `ReadAsync`) with no peeking.
  - `FieldCount` — `_columnNames.Length`.
  - `GetName(ordinal)` — `_columnNames[ordinal]`.
  - `GetOrdinal(name)` — `_projectionOrdinals[name]` (dictionary built at construction).

- Update `CouchbaseDbDataReaderTests`. Tests that call `HasRows` or `FieldCount` before `ReadAsync`
  should be adjusted to call `ReadAsync` first, or updated to reflect that `HasRows` returns false
  until the first read.

### Expected outcome

Approximately 150 lines removed. The sync-over-async call is gone. The reader's construction is
O(1) — no first-row read, no buffering, no schema inference.

---

## Phase 4 — Pre-Build Per-Row Ordinal → Value Array in `ReadAsync`

**Goal:** Make `GetValue(ordinal)` O(1) — a single array dereference — by amortizing the JSON
property scan over the whole row on read.

### Current per-row cost

For a row with `m` JSON properties and a shaper that reads `k` columns:

```
k calls × O(m) TryGetProperty scan = O(k × m) per row
```

For a typical entity with 10 columns and 10 properties this is 100 string comparisons per row.

### Proposed approach

In `ReadAsync`, after `MoveNextAsync` returns a `JsonElement` row:

1. Allocate (or reuse) a `JsonElement?[]` of length `_columnNames.Length`.
2. Do a single O(m) scan of the row's properties.
3. For each property, case-insensitively match its name against `_projectionOrdinals` and write
   the `JsonElement` value into the array at the matched ordinal. Unmatched properties are skipped.
4. Projection slots with no matching JSON property remain null (→ `DBNull.Value`).

`GetValue(ordinal)` then becomes:

```csharp
var element = _currentValues[ordinal];
return element.HasValue ? ConvertJsonElement(element.Value) : DBNull.Value;
```

### Implementation notes

- Reuse a single `_currentValues` array across rows by resetting it at the start of each
  `ReadAsync`. This eliminates per-row heap allocation.
- The array is sized at construction from `_columnNames.Length` and never resized.
- `_projectionOrdinals` (built at construction, `OrdinalIgnoreCase`) drives the name → ordinal
  mapping during the per-row scan.

### Expected outcome

Per-row cost changes from O(k × m) to O(m + k). `GetValue` is a bounds-checked array read with
no string comparisons. At 100 rows × 10 columns × 10 properties the total comparisons drop from
10,000 to ~1,000.
