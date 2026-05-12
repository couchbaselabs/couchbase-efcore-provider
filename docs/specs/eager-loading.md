# Eager Loading Implementation Plan — Couchbase EF Core

## Context

The EF Core eager loading API (`Include` / `ThenInclude`) requires a functioning query pipeline end-to-end. The current codebase is a skeleton: every method in the query pipeline throws `NotImplementedException`. Eager loading cannot be bolted on before the foundation exists, so this plan is structured in strict dependency order.

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

## Phase 2 — Include / ThenInclude Expression Handling

### How EF Core wires it up internally

When a developer writes:

```csharp
context.Blogs.Include(b => b.Posts).ThenInclude(p => p.Author)
```

EF Core calls `QueryableMethodTranslatingExpressionVisitor.TranslateInclude`. This produces an `IncludeExpression` wrapping the root `ShapedQueryExpression` with a list of `IncludeTreeNode` objects describing the navigation path and any filter lambda.

### 2.1 — Override `TranslateInclude`

In `CouchbaseQueryableMethodTranslatingExpressionVisitor`, override:

```csharp
protected override ShapedQueryExpression TranslateInclude(
    ShapedQueryExpression source,
    LambdaExpression navigationLambda,
    bool thenInclude,
    bool setLoaded)
```

Implementation steps:

1. Resolve the `INavigation` from the lambda using `navigationLambda` and the entity metadata on `source`.
2. Record the resolved navigation (and its filter lambda if present) on the `CouchbaseQueryExpression` inside `source`. Use a tree structure mirroring `IncludeTreeNode`: a root list of `NavigationInclude` nodes, each with a `Children` list for `ThenInclude` chains.
3. Return the same `source` (the include is embedded in the expression, not a new wrapper).

```csharp
// internal model to attach to CouchbaseQueryExpression
record NavigationInclude(
    INavigation Navigation,
    LambdaExpression? Filter,   // for filtered includes
    List<NavigationInclude> Children);
```

### 2.2 — Multiple root includes & ThenInclude chaining

The standard pattern:

```csharp
.Include(b => b.Posts).ThenInclude(p => p.Author)
.Include(b => b.Posts).ThenInclude(p => p.Tags)
```

EF Core calls `TranslateInclude` once per `.Include` / `.ThenInclude` call. The second `.Include(b => b.Posts)` must merge into the existing `Posts` node rather than create a duplicate. Match on `INavigation` identity before appending.

### 2.3 — Navigation chain shorthand

```csharp
.Include(b => b.Owner.AuthoredPosts)
```

EF Core decomposes this into a chain of member accesses. Walk the expression tree, resolve each navigation segment in order, and nest them as a depth-first chain of `NavigationInclude` nodes.

---

## Phase 3 — N1QL JOIN Translation

### N1QL join semantics vs SQL

Couchbase N1QL supports key-based and index-based JOINs. For a provider backed by structured collections with foreign-key style references:

```sql
-- one-to-many: Blog -> Posts
SELECT b.*, p.*
FROM `travel-sample`.`inventory`.`blog` AS b
LEFT JOIN `travel-sample`.`inventory`.`post` AS p ON b.`id` = p.`blogId`
WHERE b.`id` = @p0
```

For deeper chains (`Blog -> Posts -> Author`):

```sql
SELECT b.*, p.*, a.*
FROM `travel-sample`.`inventory`.`blog`  AS b
LEFT JOIN `travel-sample`.`inventory`.`post`   AS p ON b.`id` = p.`blogId`
LEFT JOIN `travel-sample`.`inventory`.`author` AS a ON p.`authorId` = a.`id`
```

### 3.1 — Extend `CouchbaseN1qlBuilder` to emit JOINs

In `CouchbaseN1qlBuilder.Build(CouchbaseQueryExpression expr)`:

1. Start with the root entity's keyspace as `FROM`.
2. Walk the `NavigationInclude` tree depth-first.
3. For each node, emit a `LEFT JOIN` clause:
   - Target keyspace from the navigation's target entity type's `"Couchbase:Keyspace"` annotation.
   - Alias = a stable short alias derived from the navigation name (e.g., `p0`, `p1`).
   - `ON` clause from the `IForeignKey.Properties` ↔ `IForeignKey.PrincipalKey.Properties` pairing.
4. Add the alias to the `SELECT` list (`alias.*`).

### 3.2 — Filtered includes

```csharp
.Include(b => b.Posts.Where(p => p.Published).OrderByDescending(p => p.Date).Take(5))
```

The filter lambda is attached to the `NavigationInclude.Filter`. Translate the predicate into an additional `AND` condition on the JOIN's `ON` clause. `OrderBy`/`Take` become a correlated subquery:

```sql
LEFT JOIN (
    SELECT p.*
    FROM `travel-sample`.`inventory`.`post` AS p
    WHERE p.`published` = true
    ORDER BY p.`date` DESC
    LIMIT 5
) AS p0 ON b.`id` = p0.`blogId`
```

This subquery approach keeps the main query shape consistent. Implement `TranslateWhere`, `TranslateOrderBy`, `TranslateTake` inside the nested builder pass.

### 3.3 — Split query strategy (optional, Phase 3b)

For collection navigations, a single JOIN query multiplies root rows. As an alternative, implement a **split query** strategy where a separate N1QL statement is issued per navigation, then results are joined in memory. This matches EF Core's own split-query mode and is likely better for Couchbase's document model.

```csharp
optionsBuilder.UseCouchbase(cluster, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
```

The split approach requires:
- Collecting the root entity PKs from the first query result.
- Issuing `SELECT * FROM collection WHERE foreignKeyField IN [pk1, pk2, ...]` per navigation.
- In-memory fixup (see Phase 4).

**Recommendation:** implement JOIN translation first (matches the EF Core default `SingleQuery`), then add split query as an opt-in.

---

## Phase 4 — Result Shaping & Navigation Fixup

### 4.1 — Projecting aliased columns

When the N1QL query returns `b.*`, `p.*`, etc., the JSON result will contain a map keyed by alias. The materializer must demultiplex these into separate entity instances.

In `CouchbaseShapedQueryCompilingExpressionVisitor.VisitShapedQuery`:

1. Detect whether the compiled query expression contains any `NavigationInclude` nodes.
2. If yes, emit a **multi-entity materializer** that:
   - Reads the root entity from the `b.*` JSON fragment.
   - For each `NavigationInclude`, reads the related entity from the `p0.*` fragment.
   - Uses `IEntityMaterializerSource` (already in EF Core DI) to construct typed entity instances.

### 4.2 — Collection navigation assembly

For one-to-many relationships, multiple result rows map to the same root entity. The materializer must group by root PK and aggregate the related rows into the collection property:

```
Row 1: Blog{id:1}, Post{id:10}
Row 2: Blog{id:1}, Post{id:11}
Row 3: Blog{id:2}, Post{id:12}

→ Blog{id:1, Posts:[Post{id:10}, Post{id:11}]}
→ Blog{id:2, Posts:[Post{id:12}]}
```

Implement this as a post-processing step over the raw `IAsyncEnumerable<JsonObject>` result before returning to the caller.

### 4.3 — Navigation property fixup

After materializing, call EF Core's built-in `NavigationFixer` (registered in DI via `IChangeDetector` / `INavigationFixer`) to wire inverse navigation properties and update the change tracker. For `AsNoTracking` queries, do manual fixup: set reference navigations and populate collection navigations directly on the materialized objects.

---

## Phase 5 — Auto-Include & IgnoreAutoIncludes

### 5.1 — Auto-Include

In `TranslateInclude` (Phase 2.1), after processing explicit includes, walk `IEntityType.GetNavigations()` and check `navigation.IsEagerLoaded`. For each auto-include navigation not already in the tree, call the same path-recording logic.

This must run once per entity type encountered in the query (including types brought in by earlier includes).

### 5.2 — IgnoreAutoIncludes

EF Core sets a flag on the `QueryCompilationContext` when `.IgnoreAutoIncludes()` is called. Read `QueryCompilationContext.IgnoreAutoIncludes` in the translation step and skip the auto-include injection when true.

---

## Phase 6 — Include on Derived Types

```csharp
context.People.Include(p => ((Student)p).School)
context.People.Include("School")
```

The cast-based form produces a `UnaryExpression` (cast) wrapping a `MemberExpression`. In `TranslateInclude`, detect this pattern, resolve the target type from the cast, look up the navigation on the derived `IEntityType`, and proceed normally.

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
| `Query/Internal/CouchbaseQueryableMethodTranslatingExpressionVisitor.cs` | Phase 2 | Override `TranslateInclude` to record navigation paths on the `SelectExpression` |
| `Query/Internal/CouchbaseQuerySqlGenerator.cs` | Phase 3 | Extend to emit `LEFT JOIN` clauses for navigation paths |
| `Query/Internal/NavigationInclude.cs` *(new)* | Phase 2 | `record NavigationInclude(INavigation, LambdaExpression?, List<NavigationInclude>)` |
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

Phases 2 and 3 are the most complex and should be developed together since the expression tree shape drives the N1QL output. Phase 4 (materializer) is where the most Couchbase-specific engineering lives, as JSON document structure does not directly map to flat relational rows.
