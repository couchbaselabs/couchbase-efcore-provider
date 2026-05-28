# EF Core Feature Coverage Audit — 2026-05-27

---

## Core Infrastructure & Context

**Status: Implemented**

- `DbContext` configuration and `UseCouchbase()` extension
- Bucket / Scope / Collection (keyspace) mapping via `ToCouchbaseCollection()`
- Service registration and DI (`CouchbaseServiceCollectionExtensions`)
- Conventions and model metadata handling
- Database creation and dropping (`EnsureCreatedAsync` / `EnsureDeletedAsync`)

---

## Querying & LINQ Translation

### Basic querying — Implemented

- SELECT projection and basic queries
- WHERE filtering
- Skip / Take (OFFSET / LIMIT)
- OrderBy / OrderByDescending
- First / FirstOrDefault
- Single / SingleOrDefault
- Find / FindAsync (by primary key)
- ToList / ToListAsync

### Aggregates — Partial

| Operator | Status |
|---|---|
| COUNT | ✅ |
| LongCount | ✅ |
| MAX | ✅ |
| MIN | ✅ |
| SUM | ✅ |
| Average | ❌ Known gap (NCBC-3891) |

### Joins — Implemented

- LINQ inner joins to separate collections
- Multiple joins
- Cross joins

### String functions — Implemented

Contains, IndexOf, Replace, ToUpper / ToLower, Substring (0-based → 1-based conversion),
Trim / TrimStart / TrimEnd, IsNullOrWhiteSpace

### Grouping — Implemented

- GroupBy with GROUP BY / HAVING
- Aggregate functions on groupings

### Other query operators — Partial / Unknown

| Operator | Status |
|---|---|
| Subqueries | ✅ (via relational pipeline) |
| Distinct | ✅ (inherited from relational base) |
| Union / Intersect / Except | ⚠️ Not explicitly implemented or tested |
| FromSqlRaw (string interpolation) | ✅ |
| FromSql with ADO.NET parameters | ❌ Minimal ADO.NET parameter support |

---

## Change Tracking & Updates

### SaveChanges pipeline — Implemented

- Change tracking (Add / Update / Remove)
- SaveChanges / SaveChangesAsync
- Batch CRUD in a single SaveChanges call
- Insert / Update / Delete with RETURNING clause
- Uses Couchbase Key/Value API for CRUD operations

### Bulk operations — Partial / Experimental

- ExecuteUpdate — experimental only
- ExecuteDelete — experimental only

### Transactions — Implemented

- `DbTransaction` support via `CouchbaseDbTransaction`
- Distributed transactions via Couchbase SDK
- Durability level configuration
- Rollback / commit
- Isolation levels (informational; Couchbase handles atomicity internally)

---

## Data Modeling & Configuration

### Entity types — Implemented

- Basic entity modeling
- Primary keys (multiple key types)
- Nullable / non-nullable properties
- Data annotations and Fluent API

### Inheritance — Implemented

- Table-per-hierarchy (TPH) with discriminator
- Discriminator auto-generation
- Queries against base and derived types

### Key generation — Implemented

- GUID value generation (`CouchbaseGuidStringValueGenerator`)
- Sequence-based generation (`CouchbaseSequenceValueGenerator`) for int / long / short /
  byte / uint / ulong / ushort / decimal
- `NEXT VALUE FOR` SQL++ syntax
- `UseSequence()` fluent API

### Owned entities (embedded documents) — Mostly implemented

| Feature | Status |
|---|---|
| OwnsOne — single-valued owned entities | ✅ |
| OwnsMany — collection-valued owned entities | ✅ Query projection complete |
| OwnsMany — state manager tracking | ✅ Complete (4 mechanisms; see `ownsmanycollection-state-manager-tracking.md`) |
| Navigation fixup during materialisation | ✅ Complete |

### Relationships — Implemented

- Foreign key relationships
- Reference and collection navigation properties
- HasMany / HasOne / WithOne / WithMany
- Cascade delete configuration
- Relationship state management

---

## Eager Loading

**Status: Mostly implemented — owned-entity fixup in progress**

| Feature | Status |
|---|---|
| Include() for navigation properties | ✅ |
| ThenInclude() chains | ✅ |
| Multiple Includes on same root | ✅ |
| LEFT JOINs for FK navigations | ✅ |
| Result shaping and collection grouping | ✅ |
| Navigation property population | ✅ |
| Owned-type JOINs skipped (embedded in parent) | ✅ |
| OwnsMany embedded-document fixup | ✅ Complete |
| ThenInclude chains on owned entities | ⚠️ Not yet tested |
| Auto-Include / IgnoreAutoIncludes | ❌ Planned (eager-loading Phase 5) |
| Include on derived types | ❌ Planned (eager-loading Phase 6) |

---

## Lazy Loading & Proxies

**Status: Not implemented**

- No lazy-loading support
- No proxy generation for navigation properties
- Collections are not lazy-loaded
- Explicit `Include()` required for all navigations

---

## Migrations & Schema Management

**Status: Not implemented**

- `CouchbaseHistoryRepository` — all methods throw `NotImplementedException`
- No migration tracking or history table
- No schema version management
- No automatic migration execution
- Collections must be created manually on Couchbase Server
- `EnsureCreated` performs procedural collection setup only

---

## Type Mapping & Storage

**Status: Implemented**

- `CouchbaseTypeMapping` and `CouchbaseTypeMappingSource`
- JSON element serialisation / deserialisation
- Bool, byte, byte array
- Numeric type conversions (`TONUMBER` for N1QL)
- String, DateTime, Guid
- Collection / array support via JSON arrays

---

## Features Not Implemented / Not Applicable

| Feature | Notes |
|---|---|
| Lazy loading / proxies | No proxy generation; explicit Include required |
| Migrations | No schema migration tracking or execution |
| Stored procedures | N1QL does not support stored procedures |
| Synchronous I/O | Only async; sync paths use `AsyncHelper.RunSync` (deadlock-safe — see `datareader-refactor.md` Phase 5) |
| Average aggregate | Known bug NCBC-3891 |
| Union / Intersect / Except | Not explicitly implemented or tested |
| View mapping | Couchbase collections only |
| FromSql with parameters | Requires ADO.NET parameter support (minimal in preview) |
| ExecuteUpdate / ExecuteDelete | Experimental only |
| Split queries | AsSplitQuery() not wired up |

---

## Summary

| Category | Status | Approximate coverage |
|---|---|---|
| Core CRUD & SaveChanges | ✅ Implemented | 95% |
| Basic querying (Where, Select, OrderBy) | ✅ Implemented | 95% |
| Eager loading (Include / ThenInclude) | ✅ Mostly complete | 85% (owned-entity fixup in progress) |
| Aggregations | ⚠️ Partial | 80% (no Average) |
| String functions | ✅ Implemented | 90% |
| Key generation (sequences) | ✅ Implemented | 100% |
| Transactions | ✅ Implemented | 100% |
| Relationships & navigation | ✅ Implemented | 90% |
| Owned entities | ✅ Mostly complete | 95% (write-path integration tests pending live-server verification) |
| Inheritance (TPH) | ✅ Implemented | 100% |
| Type mapping | ✅ Implemented | 90% |
| Migrations | ❌ Not implemented | 0% |
| Lazy loading | ❌ Not implemented | 0% |
| Raw SQL (FromSql) | ⚠️ Partial | 20% |
| Bulk operations (ExecuteUpdate / Delete) | ⚠️ Experimental | 30% |

---

## Key Architectural Notes

1. **Relational pipeline** — the provider uses EF Core's relational pipeline
   (`SelectExpression`, `QuerySqlGenerator`) rather than a bespoke non-relational one.
   LINQ translation is largely inherited; Couchbase-specific work is in the SQL generator
   and shaper visitor.

2. **N1QL SQL generation** — `CouchbaseQuerySqlGenerator` translates EF Core expressions to
   Couchbase SQL++ with backtick-delimited identifiers and N1QL-specific functions
   (`TONUMBER`, `CONTAINS`, etc.).

3. **Shaper-based execution** — queries execute through the full EF Core shaper pipeline
   (`CouchbaseQueryEnumerable` + `RelationalShapedQueryCompilingExpressionVisitor`) for
   correct entity materialisation and navigation fixup.

4. **Document-oriented storage** — owned entities (`OwnsOne` / `OwnsMany`) are stored as
   embedded JSON in the parent document; separate collections use foreign keys with N1QL
   JOINs.

5. **No ADO.NET parameters** — `FromSqlRaw` uses string interpolation; no parameter
   substitution is available.

6. **Async-only** — all database operations are async. Sync paths (`Read`, `Close`,
   `SaveChanges` in DEBUG) use `AsyncHelper.RunSync` which is safe under any
   `SynchronizationContext`. See `datareader-refactor.md` Phase 5 for details.
