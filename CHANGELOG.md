# Changelog

All notable changes to the EF Core Couchbase DB provider are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`Math.Min(a, b)`/`Math.Max(a, b)` and `EF.Functions.Least`/`Greatest`.** N1QL has no variadic
  `GREATEST`/`LEAST` function — the equivalent is `ARRAY_MIN`/`ARRAY_MAX`, which take a single array
  argument. Added a new `CouchbaseArrayConstantExpression` (an inline N1QL array literal,
  `[e1, e2, ...]`) and a `CouchbaseSqlTranslatingExpressionVisitor` overriding EF Core's own
  `GenerateGreatest`/`GenerateLeast` hooks to build `ARRAY_MAX`/`ARRAY_MIN` over it. Found along the
  way: EF Core's core `RelationalSqlTranslatingExpressionVisitor` intercepts `Math.Max`/`Math.Min`
  directly and calls these two hooks (returning `null`/unsupported by default) — registering them in
  an `IMethodCallTranslator` (the originally-planned approach) is dead code that's never reached.
  This also means `EF.Functions.Least`/`Greatest` (accepting any number of arguments, not just two)
  and automatic flattening of a `Math.Max(Math.Max(a, b), c)`-style chain into a single N-ary
  `ARRAY_MAX([a, b, c])` come for free. See [Supported functions](docs/Queries.md#supported-functions).
- **`[UnixMillisDateTime]`/`HasUnixMillisDateTime` — Unix-millis `DateTime` storage mode.** Stores a
  `DateTime` property as Unix epoch milliseconds (a JSON `NUMBER`) instead of this provider's
  default ISO-8601 string, for data that already uses that convention. Attaches a
  `ValueConverter<DateTime, long>` via EF Core's own standard `HasConversion` mechanism — composed
  with the existing `typeof(long)` -> `LongTypeMapping("NUMBER")` entry with no new type-mapping
  class or `FindMapping(IProperty)` override needed (confirmed via `.ToQueryString()` spike:
  `instance.TypeMapping` on a converted property already resolves correctly through EF Core's own
  generic converter-composition machinery). `.Year`/`.Month`/etc./`.Date`/`Add*` translate to
  N1QL's `DATE_PART_MILLIS`/`DATE_TRUNC_MILLIS`/`DATE_ADD_MILLIS` instead of the `_STR` family.
  Confirmed structural limitation, not fixable via smarter type propagation: comparing a
  millis-mapped property directly against `DateTime.UtcNow`/`.Now`/`.Today` now throws a clear
  `NotSupportedException` at query-translation time (previously would have silently compared a
  `NUMBER` against a string) — capture the value into a local variable before the query instead.
  See [Unix-millis DateTime storage](docs/configuration.md#unix-millis-datetime-storage).
- **`.Any(predicate)`/`.All(predicate)`/`.Count(predicate)` over a *nested* `OwnsMany` navigation**
  (reached through another owned navigation, e.g. `c.ContactMethods.Any(m => m.Tags.Any(t =>
  t.Key == "priority"))`, at any depth) — confirmed to need no new production code: the existing
  owned-collection detection is written generically per-owned-type and alias-parameterized (not
  hardcoded to the top-level navigation), so it naturally recurses for the inner shape. An indexer
  access can also appear as the innermost predicate (e.g. `c.ContactMethods.Any(m => m.Tags[0].Key
  == "priority")`). A *direct chained* indexer through two levels with no `.Any()`/`.All()`/
  `.Count()` wrapping it (e.g. `customer.ContactMethods[0].Tags[0].Key`) is confirmed **not**
  fixable at this provider's layer — it fails inside EF Core's own core query-translation code
  before any Couchbase-specific code runs, the same class of limitation as `.Contains()` below;
  use `.Any(predicate)` with an inner indexer instead. See
  [Modeling — OwnsMany](docs/modeling.md#ownsmany).
- **`.All(predicate)` and `.Count(predicate)` over a depth-1 `OwnsMany` navigation.**
  `.All(predicate)` needed no new production code — EF Core translates it as the same
  `NOT EXISTS(... WHERE NOT predicate)` shape `.Any(predicate)` already produces, so it flows
  through the existing owned-collection detection for free. `.Count(predicate)`/predicate-less
  `.Count()` translate to a correlated
  `(SELECT RAW COUNT(*) FROM parentAlias.field AS alias [WHERE predicate])[0]` subquery, mirroring
  `.Any(predicate)`'s detect/strip/render approach. Any other aggregate composed over an owned
  collection (e.g. `.Sum()`, `.Max()`) now throws a clear `NotSupportedException` instead of
  silently producing an empty-FROM-clause N1QL parse error. `.Contains()` directly on an
  `OwnsMany` navigation is confirmed **not** fixable at this provider's layer — it crashes inside
  EF Core's own core query-translation code (`RelationalSqlTranslatingExpressionVisitor.ParameterValueExtractor`)
  for any relational provider once the owned collection's key is composite, EF Core's default for
  an owned type; use `.Any(predicate)` instead. See [Modeling — OwnsMany](docs/modeling.md#ownsmany).
- **Indexer/`.ElementAt()` over a depth-1 `OwnsMany` navigation** (e.g.
  `customer.ContactMethods[0].Type`). Previously fell back to EF Core's generic correlated-subquery
  + OFFSET/LIMIT translation, which — like `.Any(predicate)` before its own fix — hit the same
  empty-FROM-clause bug (the owned collection's `TableExpression` renders as nothing) and, when
  used as a projection, additionally returned `null` because EF Core never assigns this shape a
  projection alias and the fallback alias-inference didn't recognize it either. Now translates to
  N1QL's native `parentAlias.field[index].propertyName` array subscript, mirroring
  `.Any(predicate)`'s own detect/strip/render approach. Behaves like `.ElementAtOrDefault()`
  (out-of-range returns the default rather than throwing). `.Where(...).ElementAt(...)`
  compositions and indexing into a scalar collection nested inside an owned item are not yet
  supported. See [Modeling — OwnsMany](docs/modeling.md#ownsmany).
- **Scalar primitive collections (`List<T>`/`T[]`, not `OwnsMany`).** A `List<T>`/`T[]` property
  of a scalar element type mapped directly on an entity is now stored as a native JSON array
  (previously silently double-encoded as a JSON string via EF Core's default primitive-collection
  converter, breaking any query against it) and supports indexer/`.ElementAt()` (N1QL's native
  array-subscript syntax; behaves like `.ElementAtOrDefault()` — out-of-range/negative returns the
  default value rather than throwing), `.Contains()`, `.Count`, and `.Any(predicate)` (a
  correlated subquery over the array field). `.OrderBy(...).ElementAt(...)`/
  `.Where(...).ElementAt(...)` compositions and reverse-`.Contains()` over a local in-memory
  collection are not supported. See [Modeling — Primitive collections](docs/modeling.md#primitive-collections).
- **`.Any(predicate)`/`.Any()` over a depth-1 `OwnsMany` navigation.** Previously silently
  produced invalid SQL++ (an `EXISTS` subquery with an empty `FROM` clause, since the owned
  collection is a JSON array embedded in the parent document, not a real keyspace to correlate
  against). Now translates to N1QL's `ANY x IN parentAlias.field SATISFIES ... END`. Nested owned
  collections (reached through another owned navigation), `.All(predicate)`, `.Count(predicate)`,
  and `.Contains()` over an owned collection are not yet supported. See
  [Modeling — OwnsMany](docs/modeling.md#ownsmany).
- **N1QL `META()` support: document metadata fields and CAS-based optimistic concurrency.**
  `[CouchbaseMeta(CouchbaseMetaField)]`/`HasCouchbaseMeta(...)` sources a property's value from
  `META(alias).id`/`.cas`/`.expiration` instead of a document field. Combined with EF Core's own
  `.IsConcurrencyToken()`, a `ulong` CAS property becomes a real optimistic-concurrency token:
  `SaveChangesAsync` sends the CAS on update/delete and throws `DbUpdateConcurrencyException` on a
  mismatch, closing a real gap where concurrent writes to the same document previously overwrote
  each other silently with no error. `Id`/`Expiration` are read-only. See
  [Optimistic concurrency](docs/concurrency.md).

- **`AutoCreateIndexes` option.** When enabled, `EnsureCreatedAsync` creates a primary index on
  every collection it creates or already owns, and waits for each one to come online before
  returning — closing the gap where a query issued immediately after `EnsureCreatedAsync` could
  fail because Couchbase's query service refuses to query an unindexed collection. Defaults to
  `false`. Does not create secondary indexes.
- **Broader SQL++ function translation** for LINQ queries (CBEF-23): `string.StartsWith`/
  `EndsWith` (via `LIKE`, with wildcard escaping), `string.IsNullOrEmpty`, `PadLeft`/`PadRight`,
  and `string.Length`; `Math.Abs`/`Ceiling`/`Floor`/`Round`/`Truncate`/`Pow`/`Sqrt`/`Sign`/`Log`/
  `Log10`/`Exp`; `DateTime` member access (`Year`/`Month`/`Day`/`Hour`/`Minute`/`Second`/
  `Millisecond`/`DayOfWeek`/`DayOfYear`/`Date`/`Now`/`UtcNow`/`Today`) and arithmetic
  (`AddYears`/`AddMonths`/`AddDays`/`AddHours`/`AddMinutes`/`AddSeconds`); and `Guid.NewGuid()`.
  Previously most of these either threw `InvalidOperationException` at query-compile time or (for
  `DateTime`/`Guid` member access) had no translator at all. See
  [Querying — Supported functions](docs/Queries.md#supported-functions) for the full list and what
  remains unsupported (`Math.Min`/`Max`, trig functions).
- **`DateTimeFormat` option, with a per-property override.** Configures the .NET custom `DateTime`
  format string this provider assumes when generating or comparing against `DateTime` string
  values in SQL++ — used by the `.Date`/`.Now`/`.UtcNow`/`.Today` translators and for inline
  `DateTime` literals. Defaults to `"yyyy-MM-ddTHH:mm:ss.FFFK"` (this provider's own default
  serialization), but is configurable since N1QL has no native date type and data can legitimately
  be stored in a different convention. A single property can also override the context-wide
  default independently via the `[DateTimeFormat]` attribute or the `HasDateTimeFormat` fluent API
  (applies to `.Date` and inline literals for that property only — the static `.Now`/`.UtcNow`/
  `.Today` translators have no associated property and always use the context-wide default). The
  format string supports .NET's quoted-literal (`'...'`/`"..."`) and backslash-escape (`\x`) syntax
  for embedding literal text, e.g. `"yyyy-MM-dd'T'HH:mm:ss"`. See
  [Configuration — DateTime string format](docs/configuration.md#datetime-string-format).
- **Confirmed `string.Compare`/`.CompareTo` translate correctly**, via EF Core's own base
  `ComparisonTranslator` (inherited unmodified) and this provider's inherited `CASE WHEN`
  rendering — no new provider code was needed. See
  [Querying — Supported functions](docs/Queries.md#supported-functions).

### Fixed

- **C#'s `??` (null-coalescing) generated N1QL's nonexistent `COALESCE` function**, reaching the
  server as invalid SQL++ and failing only at query-execution time, never at translation time. Now
  translates to `IFMISSINGORNULL`, which is also the semantically correct choice: a Couchbase
  document field can be genuinely missing from the JSON, not just `null`, and `IFMISSINGORNULL` is
  the only N1QL null-handling function that treats both the way `??` does.

- **`string.IndexOf` translated to N1QL's `CONTAINS`, which returns a boolean, not the integer
  position `IndexOf` must return.** Any LINQ query using `.IndexOf(...)` silently received a
  boolean masquerading as an `int`. Fixed to use `POSITION`, which matches `IndexOf`'s exact
  semantics (zero-based, `-1` if not found).

- **`CouchbaseDateTimeMemberTranslator` hardcoded a Go-layout format constant** for
  `.Date`/`.Now`/`.UtcNow`/`.Today`, assuming every `DateTime` was stored in this provider's own
  default format. Now driven by the configurable `DateTimeFormat` option instead.

- **`CouchbaseTypeMappingSource` used EF Core's stock `DateTimeTypeMapping`**, which generates a
  `TIMESTAMP 'yyyy-MM-dd HH:mm:ss.fffffff'` literal for inline `DateTime` constants — a syntax no
  N1QL date-string convention uses and likely invalid SQL++. New `CouchbaseDateTimeTypeMapping`
  generates a plain quoted string literal in the configured `DateTimeFormat` instead.

- **`CouchbaseOptionsExtensionInfo.ShouldUseSameServiceProvider`/`GetServiceProviderHashCode` did
  not account for `AutoCreateScopes`, `ScanConsistency`, `FieldNamingPolicy`, or `DateTimeFormat`**
  (in addition to the new `AutoCreateIndexes`). Two `DbContext`s that shared a connection string/
  bucket/scope/service key but differed in one of these settings were judged "equivalent" by EF
  Core and could share one internal service provider — including its singleton
  `ICouchbaseDbContextOptionsBuilder` — silently causing one context to run with the other's
  setting instead of its own. Caught via a reproducible test-suite flake while validating
  `AutoCreateIndexes` under concurrent load; fixed by including all of these in both methods, and
  `SerializerOptions` via reference equality.

## [2.0.0-beta.2] - 2026-07-15

### Added

- **Multiple buckets and clusters.** Use one `DbContext` per bucket; register multiple contexts
  via `AddCouchbase<TContext>`. When a Couchbase cluster is registered in application DI, contexts
  reuse that single shared `Cluster` (one cluster, many buckets — per Couchbase guidance) instead
  of each owning its own. For multiple physical clusters, register a keyed cluster per server
  (`AddKeyedCouchbase`) and select it per context with the new
  `CouchbaseDbContextOptionsBuilder.ServiceKey`. Falls back to the previous per-context
  cluster-ownership behavior when no application cluster is registered. See
  [Configuration](docs/configuration.md#multiple-buckets-and-clusters).
- **One `DbContext` spanning multiple buckets (same cluster).** A single context can now map
  different entities to different buckets on the same cluster. Give an entity an explicit keyspace
  with `ToCouchbaseCollection(bucket, scope, collection)` or the new three-argument
  `[CouchbaseKeyspace(bucket, scope, collection)]`; entities without an explicit bucket continue to
  use the context's configured bucket. Reads, `Find`, N1QL queries, `SaveChanges`, and
  `EnsureCreated` all resolve each entity's own bucket. Buckets must share one physical cluster
  (cross-cluster queries/transactions are not possible — use `ServiceKey` with a context per
  cluster). Multi-document transactions spanning two buckets on the same cluster are supported and
  covered by dedicated tests: a commit persists both buckets, and a rollback (or a failure partway
  through) leaves neither bucket changed. See
  [Configuration](docs/configuration.md#one-context-spanning-multiple-buckets).
- **`OwnsOne` can now read genuinely nested JSON objects**, not just the flat `owner_property`
  columns from EF Core's standard relational table-splitting. Real-world documents (including
  Couchbase's own `travel-sample` dataset) that store an owned reference as an actual nested JSON
  object — e.g. `{"geo": {"lat": ..., "lon": ...}}` rather than `{"geo_lat": ..., "geo_lon": ...}`
  — now populate correctly. This is additive: the existing flat-column round trip for documents
  the provider itself writes is unaffected.
- **`CancellationToken` support on the write path.** Tokens passed to `SaveChangesAsync` now flow
  through to the underlying Couchbase KV/query calls and are honored for real cancellation instead
  of being accepted but ignored.

### Changed

- **`SaveChangesAsync` write path parallelized.** Independent document writes within a single
  `SaveChangesAsync` call now execute concurrently (bounded concurrency) instead of one at a time,
  significantly reducing write latency for multi-entity change sets against a real cluster.
  Transactional writes remain sequential (ordered within the transaction), as required for
  correctness.

### Fixed

- **Whole numbers formatted as JSON decimals** (e.g. `"rating": 4.0`) in `int`/`long` properties no
  longer throw `FormatException` — real-world Couchbase documents (not just ones this provider
  wrote) can store an integral value with a decimal point, and both the built-in JSON conversion
  path and the provider's owned-type materializer now tolerate it.
- **`OwnsMany` items were always tracked as `Added` on load**, causing every `SaveChangesAsync` —
  even with no actual changes — to issue a spurious rewrite of the owner whenever it had an
  `OwnsMany` navigation. Collection-owned entries are no longer judged by their (meaningless, for
  this materialization path) `EntityState`; genuine changes are still reliably detected via the
  existing collection-snapshot comparison.

## [2.0.0-beta.1] - 2026-06-23

The 2.0 line is the first fully functional release of the provider. 1.0 was a
deliberately limited release; users are expected to move to 2.0. This is a prerelease —
APIs may still change before GA.

### Requirements

- **Targets .NET 10** (`net10.0`).

### Added

- **Eager loading** via `Include` / `ThenInclude` for foreign-key navigations, plus
  `AutoInclude` and `IgnoreAutoIncludes` support.
- **Owned types** — `OwnsOne` and `OwnsMany`, including nested owned types at arbitrary
  depth, embedded in the owner's document. Read and write paths both supported.
- **Filtered includes** — e.g. `Include(b => b.Posts.Where(...))`.
- **Many-to-many** — both the explicit join-entity pattern and transparent skip
  navigations (`HasMany().WithMany()`).
- **Inheritance (TPH)** — table-per-hierarchy with a discriminator: `OfType<TDerived>()`,
  `Include` on navigations declared on a derived type, owned types on a derived type, and
  `Find`/`FindAsync` resolution. Map derived types to the same collection as the base to
  opt in.
- **Value converters** on non-owned entities (`HasConversion`, including `ConvertsNulls`).
- **Query scan consistency** option — defaults to `NotBounded`; set `RequestPlus` via the
  options builder for read-after-write consistency on queries.
- **ADO.NET data reader** (`CouchbaseDbDataReader`) underpinning the query pipeline,
  including `FromSql` support.

### Changed

- Keyspace handling and resolution improvements.
- Dependency updates and build/packaging cleanup.

### Fixed

- `AVG` aggregate translation in the SQL++ generator.

### Documentation

- Filled in [`docs/limitations.md`](docs/limitations.md) with the current known
  limitations (Migrations, async-only I/O, scan consistency, owned-type requirement for
  nested data, TPH-only inheritance, supported value-generation types).

### Known limitations

See [`docs/limitations.md`](docs/limitations.md). Highlights: EF Core Migrations are not
supported (use `EnsureCreatedAsync`); synchronous query/save APIs are not supported;
TPT/TPC inheritance is not supported; nested data must be modeled as owned types.

[2.0.0-beta.2]: https://github.com/couchbaselabs/couchbase-efcore-provider/releases/tag/2.0.0-beta.2
[2.0.0-beta.1]: https://github.com/couchbaselabs/couchbase-efcore-provider/releases/tag/2.0.0-beta.1
