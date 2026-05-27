# CouchbaseDbDataReader Refactor

## Background

`CouchbaseDbDataReader<T>` is the ADO.NET `DbDataReader` bridge between the Couchbase SDK's
`IQueryResult<JsonElement>` and EF Core's compiled shapers. It was built incrementally alongside
the query pipeline and has accumulated complexity for code paths that never execute in the
provider. This document describes four sequential phases to simplify and optimize it.
Phases 1–3 are complete. Phase 4 is the next planned work.

The EF Core shaper is the sole consumer of this reader. It calls typed getters by projection
ordinal (e.g. `GetInt32(3)`, `GetString(7)`) for every column of every result row. The
projection aliases (`_columnNames`) are always supplied by `CouchbaseQueryEnumerable` from
`SelectExpression.Projection`, so no runtime schema discovery is ever needed.

---

## Phase 1 — Remove Dead Code Paths ✓

**Status: Complete**

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

### Actual outcome

Approximately 50 lines removed. The remaining code reflects only what actually runs in the
provider. Five tests required updates: the `IsType<CouchbaseDbDataReader<object>>` assertions
in both `CouchbaseCommandTests` and `CouchbaseDbDataReaderTests`, and the unit-test mock setups
that targeted `QueryAsync<object>` for the reader path. The `MultipleRows_CanIterate` integration
test also needed its N1QL query corrected from an invalid subquery alias to `UNION ALL` form.

---

## Phase 2 — Eliminate the `_fieldOrdinals` Indirection ✓

**Status: Complete**

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

### Actual outcome

The intermediate phase 2 fast path (array lookup → `_fieldOrdinals` dict → exact `TryGetProperty`)
was implemented and then superseded when phase 3 removed `_fieldOrdinals` and `_fieldNames`
entirely. The final hot path is: `_columnNames[ordinal]` → `je.TryGetPropertyCI(colName)` →
`ConvertJsonElement`. `TryGetPropertyCI` (extracted to `JsonElementExtensions`) handles
case-insensitive matching directly on the alias, eliminating the round-trip through the old
ordinal maps. Approximately 15–20 lines removed; `GetOrdinal` received comment-only cleanup.

---

## Phase 3 — Remove Schema-Discovery Infrastructure ✓

**Status: Complete**

**Goal:** Delete the first-row peek, buffering, and field-metadata machinery. `_columnNames` is
always provided so this infrastructure is never needed.

### Changes

- Make `columnNames` an optional parameter on the 2-arg constructor (`string?[]?`). A non-null
  array sets up the alias mapping; `null` falls through to the raw positional path, identical to
  using the 4-arg constructor. Remove the optional overload that omits it entirely.

- Delete the following fields and their associated logic:
  - `_bufferedRow`, `_hasBufferedRow` — first-row buffer
  - `_schemaInitialized` — initialization flag
  - `_fieldNames` — list of JSON property names from first row
  - `_fieldOrdinals` — name → position map built from first row

- Delete `EnsureFieldInfo` and `InitializeFieldInfo` entirely. These contain the only
  sync-over-async call in the reader (`MoveNextAsync().AsTask().GetAwaiter().GetResult()`).

- Simplify the properties that previously triggered schema discovery:
  - `HasRows` — see deviation note below.
  - `FieldCount` — `_columnNames.Length`.
  - `GetName(ordinal)` — `_columnNames[ordinal]`.
  - `GetOrdinal(name)` — `_projectionOrdinals[name]` (dictionary built at construction).

- Update `CouchbaseDbDataReaderTests`. Tests that previously relied on `HasRows` before
  `ReadAsync` were updated for the new `PrimeAsync` contract (see deviation below).

### Deviation: `HasRows` and `PrimeAsync`

The spec called for `HasRows` to return `_hasRows ?? false` until the first `ReadAsync` call.
In practice EF Core reads `HasRows` immediately after `ExecuteReaderAsync` returns, before
calling `ReadAsync`, so returning `false` caused silent empty-result materialisation.

To satisfy the ADO.NET contract without reintroducing the sync-over-async call:

- `internal Task PrimeAsync(CancellationToken)` was added to the reader. It eagerly advances
  the enumerator to the first row, stores the row in `_bufferedFirstRow`, sets `_hasBufferedRow`,
  and records `_hasRows = true/false`.
- `CouchbaseCommand.ExecuteDbDataReaderAsync` calls `PrimeAsync` immediately after constructing
  the reader, before returning it.
- `ReadAsync` checks `_hasBufferedRow` first; if set it drains the buffer (returns the buffered
  row, clears the fields) rather than calling `MoveNextAsync` again. This ensures the first row
  is never skipped.

### Actual outcome

Approximately 150 lines removed. The sync-over-async call is gone. `HasRows` is accurate before
the first `ReadAsync` call. Reader construction is O(1); the single async peek happens in
`PrimeAsync` on the command path. Six `PrimeAsync`-specific tests were added.

---

## Post-phase-3 corrections

Nine defects were identified and fixed after the phase 3 commit landed.

### 1 — `DbCommand.Cancel()` not propagating to the row enumerator

`PrimeAsync` was invoked with the caller's external `cancellationToken` rather than
`linkedCts.Token`, so the enumerator was bound to a token that `Cancel()` could never reach.
Additionally, `linkedCts` was declared with `using var` in `ExecuteDbDataReaderAsync`, which
disposed the link between `_cancellationTokenSource` and the enumerator's token as soon as the
method returned — making the fix incomplete even if the token were corrected.

Fix:
- Pass `linkedCts.Token` to both the reader constructor and `PrimeAsync`.
- Drop `using` from `var linkedCts`; transfer ownership to the reader via `SetLinkedCts(cts)`.
- Reader disposes the linked CTS in `Close` / `CloseAsync`.
- Guard the setup path so `linkedCts` is always disposed on failure (via the reader if it was
  created, directly otherwise).

### 2 — `PrimeAsync` not idempotent

A second `PrimeAsync` call advanced the underlying enumerator again and overwrote the buffered
first row, silently dropping data.

Fix: add an early-return guard at the top of `PrimeAsync`:
```csharp
if (_hasRows.HasValue || _hasBufferedRow || _hasCurrentRow)
    return;
```
Two tests cover double-prime and prime-after-read scenarios.

### 3 — `GetName` / `GetOrdinal` null-slot throwing wrong exception before first `Read`

In the column-names path, both methods used `if (_hasCurrentRow && ...)` to gate positional
resolution. When no row had been read the condition was false and they fell through to
`IndexOutOfRangeException("... not found")` even when the ordinal/name was in range. The
correct exception for a missing current row is `InvalidOperationException`.

Fix: replace the `_hasCurrentRow` guard with `EnsureCurrentRow()` in both methods, consistent
with the no-column-names path and each method's own remarks.

### 4 — 2-arg constructor rejecting `null` `columnNames`

`CouchbaseQueryEnumerable` computes column names as:
```csharp
var columnNames = _projectionAliases ?? _readerColumns?.Select(rc => rc?.Name).ToArray();
```
When both sources are `null` this produces `null`, which the original constructor rejected with
`ArgumentNullException`. Null is a legitimate "no alias mapping" state that should route through
the existing positional path.

Fix: change the parameter type to `string?[]?` and make alias setup conditional on non-null,
leaving `_columnNames` and `_projectionOrdinals` as `null` (positional path) when `null` is
passed. Replace the `ArgumentNullException` test with one that verifies the positional fallback.

### 5 — Non-`JsonElement` row types surfacing as `DBNull` / `IndexOutOfRangeException`

The class-level remarks documented `NotSupportedException` for non-`JsonElement` rows, but
`ReadAsync` and `PrimeAsync` assigned `_currentRow = _enumerator.Current` without validating
the type, so incompatible rows silently flowed through and surfaced as incorrect values later.

Fix: add `private static T ValidateRow(T row)` which throws `NotSupportedException` (with an
actionable message) when `row` is non-null and not a `JsonElement`. Called at both
materialization points: `_currentRow = ValidateRow(...)` in `ReadAsync` and
`_bufferedFirstRow = ValidateRow(...)` in `PrimeAsync`. `null` is allowed (see correction 7).

### 6 — `GetSchemaTable` returning empty table for scalar `SELECT RAW` rows

In the no-column-names path, `GetSchemaTable` only added rows for `JsonValueKind.Object`; a
non-object scalar `JsonElement` (e.g. `SELECT RAW 5`) produced an empty `DataTable`,
inconsistent with `FieldCount == 1` and `GetName(0) == ""`.

Fix: restructure the outer condition to `if (je.ValueKind == JsonValueKind.Object) { ... } else { scalar row }`,
emitting one row (`ColumnName=""`, `ColumnOrdinal=0`) for any non-object result.

### 7 — Null rows (`SELECT RAW null`) treated as zero-column in positional path

`ValidateRow` permits `null` rows (valid for `SELECT RAW null`), but six methods in the
no-column-names positional path treated a null `_currentRow` as a zero-column row:
`FieldCount` returned 0, `GetValue`/`GetName`/`GetOrdinal` threw `IndexOutOfRangeException`
for ordinal 0, `GetValues` computed `fieldCount = 0`, and `GetSchemaTable` returned an empty
table.

Fix: unify the scalar and null branches in all six methods so a null row behaves identically
to a non-object scalar `JsonElement`: one column, `GetName(0) = ""`, `GetValue(0) = DBNull.Value`,
`GetOrdinal(any) = 0`, `FieldCount = 1`, `GetSchemaTable` emits one row.

### 8 — `GetName` null-slot throwing `IndexOutOfRangeException` for scalar/null rows at ordinal 0

In the `_columnNames` + null-slot branch, `GetName` only handled object rows; a scalar or null
`_currentRow` fell through to `IndexOutOfRangeException` even for ordinal 0, inconsistent with
`GetValue` (which returned the scalar/`DBNull`) and the no-column-names path (which returned `""`).

Fix: add `else if (ordinal == 0) return string.Empty;` after the object-enumeration block,
covering both scalar and null rows at null-slot position 0.

### 9 — `GetOrdinal` null-slot throwing `IndexOutOfRangeException` for scalar/null rows

The same gap as correction 8 but in `GetOrdinal`'s null-slot branch: a scalar or null row
fell through to `IndexOutOfRangeException("Field not found")` rather than returning 0,
inconsistent with the no-column-names scalar path which maps any name to ordinal 0.

Fix: add `else return 0;` after the object-enumeration block, mirroring correction 8.

---

## Pre-phase-4 code review fixes

Nine additional items were raised in a code review after the post-phase-3 corrections landed.

### 1 — `_queryResult` never disposed

`IQueryResult<T>` (`BlockQueryResult<T>`) holds buffered rows and the underlying HTTP response
stream. Neither `Close` nor `CloseAsync` released it, leaving the stream open until GC
finalization.

Fix: dispose the query result after the enumerator in both paths. `CloseAsync` prefers
`IAsyncDisposable`; `Close` falls back to `IDisposable` via pattern matching.

### 2 — `PrimeAsync` failure leaves enumerator in partially-primed state

If `MoveNextAsync` threw inside `PrimeAsync` (e.g. a cluster error mid-fetch), none of
`_hasRows`, `_hasBufferedRow`, or `_hasCurrentRow` was set, but `_enumerator` was already
created. A second `PrimeAsync` call missed the idempotency guard and re-advanced the broken
enumerator.

Fix: wrap the `MoveNextAsync` body in `try/catch`; set `_hasRows = false` in the catch before
re-throwing. This poisons the idempotency guard (`_hasRows.HasValue`) so re-entry returns
immediately. Two new tests cover the failure path.

### 3 — `SetLinkedCts` had no null or double-call guards

A second `SetLinkedCts` call silently leaked the prior `CancellationTokenSource`, retargeting
`DbCommand.Cancel()` to the wrong source for the remainder of the reader's lifetime.

Fix: add `ArgumentNullException.ThrowIfNull(cts)` and an `InvalidOperationException` on
double-call. Three tests cover null, double-call, and the happy path.

### 4 — 4-arg constructor accepted but discarded `CancellationToken`

The XML doc admitted "Unused at construction; reserved for future use." Callers
(`CouchbaseCommand`) passed `linkedCts.Token` at construction expecting it to be used, but
it was silently ignored — only the token re-passed to `PrimeAsync` actually reached the
enumerator.

Fix (Option B): store the token in `_cancellationToken` at construction. `EnsureEnumerator`
uses it as a fallback when called with `CancellationToken.None`, so callers that pass a token
at construction and later call `ReadAsync(CancellationToken.None)` see correct cancellation
propagation without needing to re-pass the token. Documented with an `[EnumeratorCancellation]`
test that verifies the stored token actually reaches the enumerator.

### 5 — `FieldCount` re-walked JSON on every call (positional path)

On the no-column-names positional path, `FieldCount` called `je.EnumerateObject().Count()`
on every access, re-walking the object each time. Repeated calls per row (e.g. from
`GetSchemaTable` or user code) each paid the O(m) cost.

Fix: add `_cachedFieldCount` (int, sentinel −1). `FieldCount` populates it lazily on first
access after each row advance; `ReadAsync` resets it to −1 at the top of every advance attempt.
The `_columnNames` path (EF Core primary consumer) is unaffected — it returns
`_columnNames.Length` directly and never reaches the cache.

### 6 — Sync-over-async on `Close()` and `Read()` (documented, not yet fixed)

`Close()` calls `_enumerator?.DisposeAsync().AsTask().GetAwaiter().GetResult()`, and `Read()`
calls `ReadAsync().GetAwaiter().GetResult()`. Both can deadlock on a UI thread with a captured
`SynchronizationContext`.

Assessment: `Read()` mirrors the base-class default; EF Core never calls it. `Close()` is
reachable via `using var reader = ...` (sync `Dispose()`) even in async callers. Routing
`Close()` through `AsyncHelper.RunSync` (consistent with `CouchbaseCommand`'s sync methods)
is the recommended fix. Not yet applied; tracked for a follow-up commit.

### 7 — No regression test for `DbCommand.Cancel()` CTS ownership-transfer fix

The headline fix (CTS ownership transfer so `Cancel()` reaches the enumerator for the
reader's lifetime) had no direct test coverage. The `PrimeAsync_WithCancelledToken_*` tests
only exercise the reader directly; nothing went through `CouchbaseCommand`.

Fix: `Cancel_AfterExecuteReaderAsync_FaultsNextReadAsync` added to `CouchbaseCommandTests`.
Uses a `[EnumeratorCancellation]`-annotated async iterator so the token injected via
`GetAsyncEnumerator(linkedCts.Token)` is actually checked during `MoveNextAsync`. The
external token is `CancellationToken.None` so the only cancellation source is `command.Cancel()`.

### 8 — No tests for `PrimeAsync` + `Close`/`CloseAsync` without `ReadAsync`

Closing a primed reader without ever calling `ReadAsync` had no coverage — the buffered first
row and linked CTS cleanup path was untested.

Fix: four tests added to the `PrimeAsync` region:
- `CloseAsync` leaves `IsClosed = true` with no exception
- `CloseAsync` → `ReadAsync` returns `false` (closed-reader contract)
- `Close()` disposes the linked CTS (`Cancel()` on it throws `ObjectDisposedException`)
- `DisposeAsync()` disposes the linked CTS via the async path

---

## Phase 4 — Pre-Build Per-Row Ordinal → Value Array in `ReadAsync`

**Goal:** Make `GetValue(ordinal)` O(1) — a single array dereference — by amortizing the JSON
property scan over the whole row on read.

### Current per-row cost

After phase 3 and the pre-phase-4 code review fixes, `GetValue(ordinal)` on the EF Core path
(`_columnNames` set) is:

```
_columnNames[ordinal]          // O(1) array read → alias string
je.TryGetPropertyCI(alias)     // O(m) linear scan of JsonElement properties
ConvertJsonElement(prop)       // O(1)
```

For a row with `m` JSON properties and a shaper that reads `k` columns:

```
k calls × O(m) TryGetPropertyCI scan = O(k × m) per row
```

For a typical entity with 10 columns and 10 properties this is 100 string comparisons per row.

Note: `FieldCount` on the positional path (`_columnNames == null`) was improved to O(1) after
the first call per row via `_cachedFieldCount`. That optimization does not affect the EF Core
hot path described here, which returns `_columnNames.Length` directly.

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

### `GetValues` O(m²) fix (column-names path)

`GetValues` calls `GetValue(i)` in a loop. On the column-names path, each `GetValue` call
currently re-scans the `JsonElement` properties from the start via `TryGetPropertyCI`, making
`GetValues` O(k × m) — equivalent to the per-row cost above.

Once `_currentValues` is populated in `ReadAsync`, `GetValue(ordinal)` becomes an O(1) array
read, so `GetValues` naturally degrades to O(k) with no changes to its own implementation.
No targeted fix to `GetValues` is needed; the improvement is a free consequence of the
`_currentValues` array.

Note: the positional path (`_columnNames == null`) is not covered by Phase 4. `GetValues` on
that path remains O(m²) — each `GetValue(i)` re-walks the JSON properties from the start.
`FieldCount` is now O(1) after the first call per row (via `_cachedFieldCount`), but individual
`GetValue` calls still pay the O(m) scan. A future phase could extend `_currentValues` to the
positional path, but the positional path is not used by EF Core.

### Expected outcome

Per-row cost changes from O(k × m) to O(m + k). `GetValue` is a bounds-checked array read with
no string comparisons. `GetValues` drops from O(m²) to O(k). At 100 rows × 10 columns × 10
properties the total comparisons drop from 10,000 to ~1,000.
