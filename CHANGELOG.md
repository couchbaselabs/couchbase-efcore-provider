# Changelog

All notable changes to the EF Core Couchbase DB provider are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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

### Fixed

- **`string.IndexOf` translated to N1QL's `CONTAINS`, which returns a boolean, not the integer
  position `IndexOf` must return.** Any LINQ query using `.IndexOf(...)` silently received a
  boolean masquerading as an `int`. Fixed to use `POSITION`, which matches `IndexOf`'s exact
  semantics (zero-based, `-1` if not found).

- **`CouchbaseOptionsExtensionInfo.ShouldUseSameServiceProvider`/`GetServiceProviderHashCode` did
  not account for `AutoCreateScopes`, `ScanConsistency`, or `FieldNamingPolicy`** (in addition to
  the new `AutoCreateIndexes`). Two `DbContext`s that shared a connection string/bucket/scope/
  service key but differed in one of these settings were judged "equivalent" by EF Core and could
  share one internal service provider — including its singleton `ICouchbaseDbContextOptionsBuilder`
  — silently causing one context to run with the other's setting instead of its own. Caught via a
  reproducible test-suite flake while validating `AutoCreateIndexes` under concurrent load; fixed
  by including all of these in both methods, and `SerializerOptions` via reference equality.

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
