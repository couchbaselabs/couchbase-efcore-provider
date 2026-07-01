# Changelog

All notable changes to the EF Core Couchbase DB provider are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **One `DbContext` spanning multiple buckets (same cluster).** A single context can now map
  different entities to different buckets on the same cluster. Give an entity an explicit keyspace
  with `ToCouchbaseCollection(bucket, scope, collection)` or the new three-argument
  `[CouchbaseKeyspace(bucket, scope, collection)]`; entities without an explicit bucket continue to
  use the context's configured bucket. Reads, `Find`, N1QL queries, `SaveChanges`, and
  `EnsureCreated` all resolve each entity's own bucket. Buckets must share one physical cluster
  (cross-cluster queries/transactions are not possible — use `ServiceKey` with a context per
  cluster). See [Configuration](docs/configuration.md#one-context-spanning-multiple-buckets).

## [2.0.0-beta.2] - 2026-06-24

### Added

- **Multiple buckets and clusters.** Use one `DbContext` per bucket; register multiple contexts
  via `AddCouchbase<TContext>`. When a Couchbase cluster is registered in application DI, contexts
  reuse that single shared `Cluster` (one cluster, many buckets — per Couchbase guidance) instead
  of each owning its own. For multiple physical clusters, register a keyed cluster per server
  (`AddKeyedCouchbase`) and select it per context with the new
  `CouchbaseDbContextOptionsBuilder.ServiceKey`. Falls back to the previous per-context
  cluster-ownership behavior when no application cluster is registered. See
  [Configuration](docs/configuration.md#multiple-buckets-and-clusters).

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
