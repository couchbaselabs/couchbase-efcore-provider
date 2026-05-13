# Eager Loading Implementation Plan — Couchbase EF Core

## Context

The EF Core eager loading API (`Include` / `ThenInclude`) requires a functioning query pipeline end-to-end. This plan is structured in strict dependency order. Phases 0, 2, and 3 are complete; the query pipeline executes N1QL queries through the EF Core relational shaper and materialises entity graphs from JOIN results. Phase 4 (owned-type / embedded-document fixup) and later phases remain.

---

## Phase 0 — Prerequisites (Query Pipeline Foundation) ✅ COMPLETE

Eager loading translates `Include` expressions into N1QL JOINs and then shapes the result rows back into an entity graph. None of that is possible until the basic pipeline can execute a simple `SELECT` query. These items are out of scope for eager loading itself but must be completed first.

### Architecture note

The provider uses EF Core's **relational pipeline** (`Microsoft.EntityFrameworkCore.Relational`), not a custom non-relational one. This means:

- `SelectExpression` (EF Core's own) is the query IR — not a bespoke `CouchbaseQueryExpression`.
- `CouchbaseQuerySqlGenerator` (extends `QuerySqlGenerator`) translates `SelectExpression` into N1QL.
- `CouchbaseQueryEnumerable<T>` executes the N1QL via `cluster.QueryAsync<T>()` and handles identity resolution.
- `CouchbaseShapedQueryCompilingExpressionVisitor` (extends `RelationalShapedQueryCompilingExpressionVisitor`) orchestrates materialisation.
- N1QL keyspace (`bucket.scope.collection`) is set via `ToCouchbaseCollection()` / `ToTable()` on the entity type; `CouchbaseQuerySqlGenerator` rewrites the table reference into N1QL format.

### Completed tasks

| # | Task | File(s) |
|---|------|---------|
| 0.1 | `CouchbaseQueryEnumerable<T>` — executes N1QL via `cluster.QueryAsync<T>()` with identity resolution | `Query/Internal/CouchbaseQueryEnumerable.cs` |
| 0.2 | `CouchbaseQuerySqlGenerator` — extends `QuerySqlGenerator`; emits N1QL `SELECT RAW`, `FROM bucket.scope.collection AS alias`, `LEFT JOIN` skipping owned types, `TONUMBER` for unary, N1QL-style `LIMIT`/`OFFSET` | `Query/Internal/CouchbaseQuerySqlGenerator.cs` |
| 0.3 | `CouchbaseShapedQueryCompilingExpressionVisitor.VisitShapedQuery` — wires together `ShaperProcessingExpressionVisitor`, `relationalCommandResolver`, and `CouchbaseQueryEnumerable` | `Query/Internal/CouchbaseShapedQueryCompilingExpressionVisitor.cs` |
| 0.4 | `CouchbaseQueryableMethodTranslatingExpressionVisitor` — changed base class to `RelationalQueryableMethodTranslatingExpressionVisitor`; `TranslateWhere`, `TranslateSelect`, `TranslateTake`, `TranslateSkip`, and all standard LINQ operators are now **inherited** from the relational base | `Query/Internal/CouchbaseQueryableMethodTranslatingExpressionVisitor.cs` |
| 0.5 | `CouchbaseQueryableMethodTranslatingExpressionVisitorFactory` — injects `RelationalQueryableMethodTranslatingExpressionVisitorDependencies`; registered in DI; previously excluded from compilation via `<Compile Remove>` entries that have been removed | `Query/Internal/CouchbaseQueryableMethodTranslatingExpressionVisitorFactory.cs`, `Couchbase.EntityFrameworkCore.csproj`, `Extensions/CouchbaseServiceCollectionExtensions.cs` |

---

## Phase 1 — Navigation Metadata & Model Configuration

EF Core stores relationship metadata on `IEntityType` / `INavigation`. The provider needs to read that metadata correctly and allow developers to declare relationships.

### 1.1 — Relationship configuration in `OnModelCreating`

Developers configure navigations the standard EF way. No provider code is needed for the fluent API itself; it is inherited. What must work:

```csharp
modelBuilder.Entity<Blog>()
    .HasMany(b => b.Posts)
    .WithOne(p => p.Blog)
    .HasForeignKey(p => p.BlogId);
```

**Acceptance criterion:** `IEntityType.GetNavigations()` and `INavigation.ForeignKey` return the correct metadata at runtime.

### 1.2 — Keyspace mapping

Couchbase collections map to EF entity types. Each entity type must know its fully-qualified N1QL keyspace (`bucket`.`scope`.`collection`). Add a convention or fluent extension:

```csharp
// option A — convention: use entity simple name as collection name
// option B — explicit
modelBuilder.Entity<Post>().ToCouchbaseCollection("travel-sample", "inventory", "post");
```

**New file:** `Extensions/CouchbaseEntityTypeBuilderExtensions.cs`  
**New annotation key:** `"Couchbase:Keyspace"`

### 1.3 — Auto-Include model configuration

EF Core calls `Navigation(...).AutoInclude()` on the model builder. The provider must not suppress this signal. Verify that `INavigationBase.AutoInclude()` metadata is readable via `INavigation.IsEagerLoaded` (EF8 internal). No provider code required beyond confirming the metadata flows through.

---

## Phase 2 — Include / ThenInclude Expression Handling ✅ COMPLETE

### How EF Core 10 wires it up (implementation note)

**EF Core 10 does not have a `TranslateInclude` method.** The spec was written against an older conceptual model. Actual pipeline in EF Core 10:

1. `NavigationExpandingExpressionVisitor` processes `.Include()` / `.ThenInclude()` calls and records the navigation tree in each entity reference's `IncludePaths`.
2. During navigation expansion, `IncludeExpression` nodes are created in the `ShapedQueryExpression.ShaperExpression` — one per navigation level, chained through `EntityExpression`.
3. The base `RelationalQueryableMethodTranslatingExpressionVisitor` adds the SQL LEFT JOINs as part of normal expression translation (inherited; no Couchbase override needed for standard foreign-key navigations).
4. `ShaperProcessingExpressionVisitor.ProcessShaper` (called in `CouchbaseShapedQueryCompilingExpressionVisitor.VisitShapedQuery`) processes the `IncludeExpression` nodes and generates navigation fixup code.
5. For owned types (embedded in documents), JOINs are intentionally skipped by `CouchbaseQuerySqlGenerator.VisitLeftJoin` / `VisitInnerJoin` — Phase 4 handles the embedded-document read.

### 2.1 — `NavigationInclude` record

```csharp
// Query/Internal/NavigationInclude.cs
public record NavigationInclude(
    INavigation Navigation,
    LambdaExpression? Filter,   // always null — see note below
    List<NavigationInclude> Children);
```

Stored on `CouchbaseQueryCompilationContext.NavigationIncludes` for Phase 4 consumption.

> **Filter is always `null`.** By the time `VisitShapedQuery` runs, any filter lambda from
> `.Include(b => b.Posts.Where(...))` has already been compiled into a SQL predicate inside
> the `SelectExpression`'s WHERE clause and is no longer recoverable as a `LambdaExpression`
> from `IncludeExpression.NavigationExpression`. The field is retained in the record for
> forward compatibility, but Phase 4 **must not** rely on it to detect filtered includes.
> Instead, Phase 4 should inspect `RelationalCollectionShaperExpression` or the inner
> `SelectExpression`'s predicate to determine whether a collection include is filtered.

### 2.2 — Include collection in `VisitShapedQuery`

`CouchbaseShapedQueryCompilingExpressionVisitor.VisitShapedQuery` calls `CollectNavigationIncludes(shaperExpression)` before compiling the shaper, which delegates to the internal static `PopulateNavigationIncludes(shaperExpression, target)`. This walks the outermost `IncludeExpression` chain in the shaper and records root-level `INavigation` includes on the context. `ISkipNavigation` (many-to-many) entries are skipped. ThenInclude chains (nested inside collection/reference shaper expressions) are resolved by Phase 4.

`CouchbaseQueryCompilationContextFactory` is registered via the remove-then-add pattern in `AddEntityFrameworkCouchbase` to ensure EF Core's relational default is replaced; without this, `CollectNavigationIncludes` silently no-ops because the context type check fails.

### 2.3 — Deduplication and ThenInclude chaining

EF Core's `NavigationExpandingExpressionVisitor` already deduplicates includes (merges `.Include(b => b.Posts).Include(b => b.Posts)` into one `IncludeTreeNode`). No Couchbase-side deduplication is required. ThenInclude children are visible inside the `NavigationExpression` of each `IncludeExpression` — Phase 4 will walk these recursively.

---

## Phase 3 — Shaper-Based Query Execution ✅ COMPLETE

### Architecture note (replaces the original CouchbaseN1qlBuilder design)

Because the provider uses the **relational pipeline** (established in Phase 0), JOIN SQL generation is already handled by `CouchbaseQuerySqlGenerator.VisitLeftJoin` — it inherits the EF Core relational base class behaviour and emits N1QL `LEFT JOIN` clauses with the correct backtick-delimited keyspace and `ON` condition.

The critical gap identified in Phase 3 was that `CouchbaseQueryEnumerable<T>.GetAsyncEnumerator` **bypassed the EF Core shaper entirely**. It called `cluster.QueryAsync<T>()` and manually materialised entities via reflection/identity-resolution. This meant:
- The shaper's entity-materialisation and navigation-fixup code never ran.
- JOIN results (multi-column rows) couldn't be deserialised as `T` since they weren't single-document shapes.
- Collection navigation assembly was impossible.

### What was implemented

#### 3.1 — `CouchbaseQueryEnumerable<T>.GetAsyncEnumerator` rewrite

`GetAsyncEnumerator` now follows the EF Core `SingleQueryingEnumerable<T>` pattern:

1. Issues `cluster.QueryAsync<JsonElement>()` to get raw JSON rows (not pre-deserialised as `T`).
2. Wraps the result in `CouchbaseDbDataReader<JsonElement>`, which maps JSON property names to ordinals so the EF Core shaper can read column values by ordinal.
3. Drives `SingleQueryResultCoordinator` with the exact pattern used by EF Core's own `SingleQueryingEnumerable`:
   - Sets `coordinator.ResultReady = true` and `coordinator.HasNext = null` **before** each shaper call (signal: row is available).
   - Calls the `_shaper` lambda (generated by `RelationalShapedQueryCompilingExpressionVisitor`).
   - If `ResultReady` is still `true` after the call → entity is complete, yield it and read the next row.
   - If `ResultReady` is `false` and `HasNext == true` → shaper buffered the current row (new root key detected mid-stream), loop without calling `ReadAsync`.
   - If `ResultReady` is `false` and `HasNext == null` → shaper is still accumulating a collection, read the next row.
   - On EOF: sets `HasNext = false`, `ResultReady = true`, calls shaper once more to flush the last pending root entity.

#### 3.2 — `CouchbaseDbDataReader<T>` scalar-result fix

`SELECT RAW COUNT(*)` returns a bare JSON primitive (not a JSON object). `InitializeFieldInfo` previously fell through to reflection on the `JsonElement` struct, producing garbage column metadata. Fix: when the row is a non-Object `JsonElement`, add a single synthetic empty-string field at ordinal 0. `GetOrdinal(anyName)` then falls back to ordinal 0 for these single-synthetic-field readers, so the EF Core scalar shaper can use any column alias and still read the value.

#### 3.3 — N1QL response field-ordering fix

N1QL returns response JSON fields in **document-storage order**, not in SELECT clause order. Positional ordinal access (`_fieldNames[ordinal]`) therefore maps to the wrong JSON property for any entity whose document field order differs from the SELECT projection order.

Fix — two coordinated changes:

1. **`CouchbaseShapedQueryCompilingExpressionVisitor.VisitShapedQuery`** — extracts the ordered list of effective SELECT projection aliases from `selectExpression.Projection` and passes it to `CouchbaseQueryEnumerable` as a `string[] projectionAliases` liftable constant. The effective alias for a projection is `ProjectionExpression.Alias` when non-empty, or `ColumnExpression.Name` when the alias is `""` (EF Core's sentinel for "no explicit AS clause needed"). In EF Core 10, `readerColumns` returned by `ProcessShaper` can be null for certain query shapes, making `_readerColumns?.Select(rc => rc?.Name)` an unreliable fallback — `projectionAliases` is always populated and is now the authoritative source.

2. **`CouchbaseDbDataReader<T>`** — when `_columnNames` is supplied, the reader maintains two ordinal spaces that must stay consistent across all public members:
   - **Projection ordinal** — the index in `_columnNames`; what the EF Core shaper passes to `GetValue`, `GetName`, `GetOrdinal`.
   - **JSON ordinal** — the index in `_fieldNames`; the document-storage order returned by N1QL.

   Key implementation details:
   - `_projectionOrdinals` (built at construction) is a `Dictionary<string, int>(OrdinalIgnoreCase)` mapping non-null alias → projection ordinal, giving O(1) `GetOrdinal` without requiring schema discovery.
   - `GetValue(ordinal)` translates shaper ordinal → alias → `_fieldOrdinals[alias]` → JSON value. Fields absent from the N1QL response (MISSING) return `DBNull.Value`. Null slots in `_columnNames` fall back to positional access (`_fieldNames[ordinal]`); if the JSON response has fewer fields than the null-slot ordinal, `DBNull.Value` is returned rather than throwing. Without `_columnNames`, an out-of-range ordinal is programmer error and throws.
   - `GetOrdinal(name)` resolves against `_projectionOrdinals` first. If not found, a constrained fallback checks `_fieldOrdinals` and accepts the result only when the matching JSON ordinal is itself a null slot (`_columnNames[jsonOrd] == null`). This preserves the `GetOrdinal(GetName(i)) == i` inverse for null-slot positions without opening arbitrary JSON-field lookups that would cause `GetValue` to index into the wrong projection slot.
   - `GetName(ordinal)` returns the projection alias for non-null slots; for null slots it falls back to `_fieldNames[ordinal]` (the JSON field name), consistent with `GetValue`'s positional semantics.
   - `FieldCount`, `GetValues`, and `GetSchemaTable` all use `_columnNames.Length` as the authoritative field count when a projection mapping is active.

### N1QL sample output for a JOIN query

```sql
-- context.Blogs.Include(b => b.Posts) produces (with UseCamelCaseNamingConvention):
SELECT `b`.`id`, `b`.`name`, `p`.`id` AS `id0`, `p`.`title`, `p`.`blogId`
FROM `travel-sample`.`inventory`.`blog` AS `b`
LEFT JOIN `travel-sample`.`inventory`.`post` AS `p` ON `b`.`id` = `p`.`blogId`
```

The SQL is generated entirely by `CouchbaseQuerySqlGenerator` (inherited relational behaviour). No bespoke JOIN builder is needed.

`CouchbaseSqlGenerationHelper` wraps all identifiers in backticks. Backtick identifiers in SQL++ are **case-sensitive**, so `UseCamelCaseNamingConvention()` must be applied when configuring the context — otherwise EF Core generates PascalCase column names (e.g., `` `Id` ``) that don't match lowercase Couchbase document fields (`id`), causing all projected values to be MISSING.

### 3.4 — Split query strategy (optional, future Phase 3b)

For collection navigations, a single JOIN query multiplies root rows. A **split query** strategy (separate N1QL statement per navigation, results joined in memory) may be better suited to Couchbase's document model. This matches EF Core's own `QuerySplittingBehavior.SplitQuery` opt-in and is deferred to a future phase.

---

## Phase 4 — Owned-Type / Embedded-Document Navigation Fixup

### Context (updated after Phase 3)

Standard foreign-key navigations between separate collections are **already handled by Phase 3**:

- Projection ordinals and column aliasing — handled by `CouchbaseDbDataReader` + `projectionAliases` liftable constant.
- Collection grouping (multiple rows → single root entity with populated collection) — handled by `SingleQueryResultCoordinator` in `CouchbaseQueryEnumerable`.
- Navigation property fixup (inverse references, change tracker) — generated by `ShaperProcessingExpressionVisitor.ProcessShaper` inside the EF Core shaper.

Phase 4 is therefore scoped to **owned types stored as embedded JSON within the parent document**, where the Phase 3 JOIN approach does not apply.

### 4.1 — Owned-type detection

`CouchbaseQuerySqlGenerator.VisitLeftJoin` / `VisitInnerJoin` intentionally skips JOINs for owned-type navigations (they live inside the parent document, not in a separate collection). `NavigationIncludes` recorded by Phase 2 will contain these navigations. Phase 4 must distinguish them from foreign-key navigations.

Detection: check `navigation.TargetEntityType.IsOwned()` on each `NavigationInclude` entry.

### 4.2 — Embedded-document materialisation

For owned navigations, the parent document's JSON already contains the nested object(s). Phase 4 must:

1. Locate the embedded JSON fragment within the parent document's `JsonElement` using the navigation's configured column name or JSON property path.
2. Materialise the owned entity from that fragment using `IEntityMaterializerSource`.
3. Assign the materialised instance to the navigation property on the parent entity.

For owned collections, the fragment is a JSON array; iterate and materialise each element.

### 4.3 — ThenInclude chain walking

`NavigationIncludes` recorded in Phase 2 only captures root-level navigations. Phase 4 must recursively walk `NavigationInclude.Children` to process ThenInclude chains, applying the same owned-vs-foreign-key detection at each level.

---

## Phase 5 — Auto-Include & IgnoreAutoIncludes

### 5.1 — Auto-Include

EF Core's `NavigationExpandingExpressionVisitor` already injects auto-include navigations (those with `navigation.IsEagerLoaded == true`) into the `IncludeExpression` chain before `VisitShapedQuery` is called. No Couchbase-side injection is needed for standard foreign-key navigations.

For owned-type auto-includes (Phase 4 scope), verify that `NavigationIncludes` captures them correctly and that Phase 4's embedded-document materialisation handles them without an explicit `.Include()` call.

### 5.2 — IgnoreAutoIncludes

EF Core's `NavigationExpandingExpressionVisitor` respects `QueryCompilationContext.IgnoreAutoIncludes` before building the `IncludeExpression` chain. No Couchbase-side handling is required for foreign-key navigations. For owned-type navigations, check whether the flag suppresses their `IncludeExpression` nodes or whether Phase 4 must check it explicitly.

---

## Phase 6 — Include on Derived Types

```csharp
context.People.Include(p => ((Student)p).School)
context.People.Include("School")
```

The cast-based form produces a `UnaryExpression` (cast) wrapping a `MemberExpression`. EF Core's `NavigationExpandingExpressionVisitor` resolves this to the correct derived-type navigation and creates the `IncludeExpression` node — no Couchbase-side detection is needed for foreign-key navigations. For owned-type navigations on derived types, Phase 4 must handle the cast when walking `NavigationIncludes`.

The string-based overload (`Include(string navigationName)`) is resolved by EF Core's `QueryableExtensions` before reaching the visitor. No special provider handling is needed beyond correct navigation metadata on the model.

---

## File Map

| File | Status | Change |
|------|--------|--------|
| `Query/Internal/CouchbaseQueryableMethodTranslatingExpressionVisitor.cs` | ✅ Phase 0 | Now extends `RelationalQueryableMethodTranslatingExpressionVisitor`; `CreateSubqueryVisitor` override; all standard LINQ ops inherited |
| `Query/Internal/CouchbaseQueryableMethodTranslatingExpressionVisitorFactory.cs` | ✅ Phase 0 | Injects `RelationalQueryableMethodTranslatingExpressionVisitorDependencies`; registered in DI |
| `Query/Internal/CouchbaseShapedQueryCompilingExpressionVisitor.cs` | ✅ Phase 0 | `VisitShapedQuery` wires `ShaperProcessingExpressionVisitor` + `CouchbaseQueryEnumerable` |
| `Query/Internal/CouchbaseQuerySqlGenerator.cs` | ✅ Phase 0 | N1QL generation from `SelectExpression` (RAW, keyspace, JOIN, LIMIT/OFFSET) |
| `Query/Internal/CouchbaseQueryEnumerable.cs` | ✅ Phase 0 | Executes N1QL via `cluster.QueryAsync<T>()` with identity resolution |
| `Query/Internal/CouchbaseQueryEnumerable.cs` | ✅ Phase 3 | Rewritten: uses `QueryAsync<JsonElement>` + `CouchbaseDbDataReader<JsonElement>` + EF Core shaper + `SingleQueryResultCoordinator`; carries `string[] projectionAliases` from `VisitShapedQuery` |
| `Query/Internal/CouchbaseShapedQueryCompilingExpressionVisitor.cs` | ✅ Phase 3 | `VisitShapedQuery` extracts ordered projection aliases via `EffectiveAlias`; passes as liftable constant to `CouchbaseQueryEnumerable` |
| `Storage/Internal/CouchbaseDbDataReader.cs` | ✅ Phase 3 | `InitializeFieldInfo` handles scalar non-Object `JsonElement` (single synthetic `""` field); `GetOrdinal` falls back to ordinal 0 for scalar results; when `_columnNames` is active: `_projectionOrdinals` reverse-lookup (O(1), built at construction); `GetValue` translates shaper ordinal → alias → `_fieldOrdinals` → JSON value, with null-slot positional fallback and MISSING → `DBNull.Value`; `GetOrdinal` resolves aliases via `_projectionOrdinals` with constrained null-slot fallback to restore `GetOrdinal(GetName(i)) == i`; `GetName` returns alias for non-null slots, JSON field name for null slots; `FieldCount`/`GetValues`/`GetSchemaTable` use `_columnNames.Length` |
| `Query/Internal/CouchbaseQueryableMethodTranslatingExpressionVisitor.cs` | ✅ Phase 2 | No override needed — base relational pipeline handles SQL JOINs for navigations automatically |
| `Query/Internal/NavigationInclude.cs` *(new)* | ✅ Phase 2 | `record NavigationInclude(INavigation, LambdaExpression?, List<NavigationInclude>)` — Phase 4 consumption target |
| `Query/Internal/CouchbaseQueryCompilationContext.cs` | ✅ Phase 2 | Extends `RelationalQueryCompilationContext`; adds `NavigationIncludes` list and precompiling constructor overload |
| `Query/Internal/CouchbaseQueryCompilationContextFactory.cs` *(new)* | ✅ Phase 2 | Implements `IQueryCompilationContextFactory`; creates `CouchbaseQueryCompilationContext`; registered via remove-then-add in `AddEntityFrameworkCouchbase` |
| `Query/Internal/CouchbaseShapedQueryCompilingExpressionVisitor.cs` | ✅ Phase 2 | `CollectNavigationIncludes` → `PopulateNavigationIncludes` (internal static, testable seam) → `ExtractNavigationIncludes`; walks root-level `IncludeExpression` chain; skips `ISkipNavigation` |
| `Extensions/CouchbaseEntityTypeBuilderExtensions.cs` | Phase 1 | `ToCouchbaseCollection(bucket, scope, collection)` already exists; verify annotation flows |

---

## Acceptance Tests to Write

Each maps directly to a pattern from the [EF Core eager loading docs](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager):

1. **Basic include** — `context.Blogs.Include(b => b.Posts)` returns blogs with populated `Posts`.
2. **Multiple includes** — `.Include(b => b.Posts).Include(b => b.Owner)`.
3. **ThenInclude chain** — `.Include(b => b.Posts).ThenInclude(p => p.Author)`.
4. **Deep chain** — `.Include(b => b.Posts).ThenInclude(p => p.Author).ThenInclude(a => a.Photo)`.
5. **Filtered include** — `.Include(b => b.Posts.Where(p => p.Published).Take(5))`.
6. **Auto-include** — configure `AutoInclude()` on model; verify navigation populated without explicit `Include`.
7. **IgnoreAutoIncludes** — verify auto-included navigation is absent when suppressed.
8. **Include on derived type** — `context.People.Include(p => ((Student)p).School)`.

---

## Recommended Sequence

```
Phase 0 (foundation) → Phase 1 (metadata) → Phase 2 (expression handling)
    → Phase 3a (single-query JOINs) → Phase 4 (materializer)
    → Phase 5 (auto-include) → Phase 6 (derived types)
    → Phase 3b (split query, optional)
```

Phases 0–3 are complete. Phase 4 (owned-type / embedded-document fixup) is where the remaining Couchbase-specific engineering lives — foreign-key navigation materialisation is already handled by the EF Core shaper pipeline established in Phase 3.
