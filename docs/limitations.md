# Limitations

The EF Core Couchbase DB provider maps EF Core onto Couchbase using SQL++ (N1QL)
and the Key/Value API. The large majority of EF Core concepts work as described in
[the EF Core documentation](https://learn.microsoft.com/en-us/ef/core/) and in the
other pages of this guide. This page lists the areas that are **not** supported, or
that behave differently from a relational provider, in the current 2.0 pre-release.

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

See also [Modeling](modeling.md).

## Asynchronous I/O only

  is asynchronous, so the synchronous code paths throw `NotSupportedException` in Release builds. Use the
  is asynchronous, so the synchronous code paths throw `NotSupportedException`. Use the
  async variants throughout: `ToListAsync`, `FirstAsync`, `SingleAsync`,
  `FindAsync`, `SaveChangesAsync`, `EnsureCreatedAsync`, and so on.

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

## Inheritance

* **Table-per-hierarchy (TPH) is supported.** Derived types share a single collection
  and are distinguished by a discriminator property. `OfType<TDerived>()`, `Include`
  on a navigation declared on a derived type, owned types on a derived type, and
  `Find`/`FindAsync` all work with TPH. Map the derived types to the same collection
  as the base type to opt into TPH.
* **Table-per-type (TPT) and table-per-concrete-type (TPC) are not supported.**

## Value generation and keys

* **Sequence-based value generation supports integer types only:** `int`, `long`,
  `short`, `byte`, `uint`, `ulong`, `ushort`, and `decimal`. Other CLR types throw at
  model build / value-generation time.
* **Generated `Guid` primary keys are partially supported** — see
  [Modeling](modeling.md) for details.

---

> [!NOTE]
> This list reflects the current pre-release. As the provider evolves, items here may
> become supported; check the release notes and the other pages in this guide for the
> latest behavior.
