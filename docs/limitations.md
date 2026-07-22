# Limitations

The EF Core Couchbase DB provider maps EF Core onto Couchbase using SQL++ (N1QL)
and the Key/Value API. The large majority of EF Core concepts work as described in
[the EF Core documentation](https://learn.microsoft.com/en-us/ef/core/) and in the
other pages of this guide. This page lists the areas that are **not** supported, or
that behave differently from a relational provider, as of the `2.0.0-beta.2` release.

Several of these stem from Couchbase being a document database: features that are
specific to relational schemas (migrations, views, stored procedures, table schema)
have no Couchbase equivalent.

## Schema management and Migrations

* **EF Core Migrations are not supported.** The provider has no migration history
  store. Create your bucket/scope/collections from the model with
  `context.Database.EnsureCreatedAsync()` instead. Schema changes are managed outside
  of EF Core (or by re-running `EnsureCreatedAsync`).

* **Relational schema concepts do not apply:** table schema, view mapping, and stored
  procedures have no Couchbase counterpart. Attempting to map an entity DML operation
  to a stored procedure throws `NotSupportedException`.

* **`EnsureCreatedAsync` does not create secondary indexes.** It creates the bucket's scopes and
  collections (and any configured sequences — see [Sequences](sequences.md)). LINQ,
  `FromSqlRaw`/`FromSql`, and `ExecuteUpdate`/`ExecuteDelete` all run as SQL++ (N1QL) queries
  under the hood, and Couchbase's query service refuses to query a collection that has no primary
  or secondary index at all — set `AutoCreateIndexes = true` on the options builder to have
  `EnsureCreatedAsync` also create a primary index on every collection it creates or already owns,
  waiting for each one to come online before returning (see
  [Configuration](configuration.md#ef-core-couchbase-db-provider-options)). This defaults to
  `false`, so by default you must still create at least a primary index yourself:

  ```sql
  CREATE PRIMARY INDEX IF NOT EXISTS ON `bucket`.`scope`.`collection`
  ```

  A primary index is enough to get started but scans the whole collection; for real workloads,
  create secondary indexes on the fields you filter/sort/join by instead
  (`CREATE INDEX ix_name ON \`bucket\`.\`scope\`.\`collection\`(field)`) — the provider does not
  automate secondary index creation from your model (there's no equivalent of EF Core's
  `HasIndex()` support yet). See the [`CreatePrimaryIndexesAsync` helper in the Contoso University
  sample](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/samples/ContosoUniversity/Program.cs)
  for a worked example of the pattern `AutoCreateIndexes` now automates.

See also [Modeling](modeling.md).

## Asynchronous I/O only

The Couchbase SDK is asynchronous, so the synchronous EF Core code paths throw `NotSupportedException` in Release builds. Use the async variants throughout: `ToListAsync`, `FirstAsync`, `SingleAsync`, `FindAsync`, `SaveChangesAsync`, `EnsureCreatedAsync`, and so on.
## Querying and consistency

* **Query scan consistency defaults to `NotBounded`.** Because secondary (GSI) indexes
  are updated asynchronously, a document that was just written may not immediately be
  visible to a subsequent SQL++ query. When you need read-after-write consistency for
  queries, set `RequestPlus` on the options builder:

  ```csharp
  optionsBuilder.UseCouchbase(clusterOptions, o =>
  {
      o.Bucket = "MyBucket";
      o.Scope = "MyScope";
      o.ScanConsistency = Couchbase.Query.QueryScanConsistency.RequestPlus;
  });
  ```

  Stronger consistency increases query latency, so it is opt-in rather than the default.
* **Nested data must be modeled as owned types.** Nested objects and collections are
  persisted and queried only when configured as EF Core owned types (`OwnsOne` /
  `OwnsMany`) or as related entities. Plain CLR objects nested on an entity that are
  not mapped this way are ignored by EF Core.

See also [Querying](Queries.md) and [Configuration](configuration.md).

## Buckets and contexts

* **A `DbContext` can span multiple buckets only within one cluster.** Entities default to the
  context's configured bucket, but individual entities can be mapped to other buckets on the
  **same** cluster (via `ToCouchbaseCollection(bucket, scope, collection)` or the three-argument
  `[CouchbaseKeyspace]`). Buckets on **different** physical clusters cannot be mixed in one
  context — a single N1QL query or transaction cannot span clusters. Use one `DbContext` per
  cluster (with keyed `ServiceKey` registration) for that. Multiple contexts can also share a
  single `Cluster`. See [Configuration](configuration.md#multiple-buckets-and-clusters).

## Inheritance

* **Table-per-hierarchy (TPH) is supported.** Derived types share a single collection
  and are distinguished by a discriminator property. `OfType<TDerived>()`, `Include`
  on a navigation declared on a derived type, owned types on a derived type, and
  `Find`/`FindAsync` all work with TPH. Map the derived types to the same collection
  as the base type to opt into TPH.
* **Table-per-type (TPT) and table-per-concrete-type (TPC) are not supported.**

## Value generation and keys
* **Sequence-based value generation supports a fixed set of numeric types:** `int`, `long`,
  `short`, `byte`, `uint`, `ulong`, `ushort`, and `decimal`. Other CLR types throw at
  model build / value-generation time. See [Sequences and generated values](sequences.md).
* **Generated `Guid` primary keys are supported via `UseGuid()`/`UseGuidString()`** — see
  [Sequences and generated values](sequences.md#generated-guids) for details.

---

> [!NOTE]
> This list reflects the `2.0.0-beta.2` prerelease. As the provider evolves, items here
> may become supported; check the release notes and the other pages in this guide for
> the latest behavior.
