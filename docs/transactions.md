# Transactions

The provider supports real Couchbase multi-document transactions through
`DatabaseFacade.BeginCouchbaseTransactionAsync`/`BeginCouchbaseTransaction`, giving a group of
`SaveChanges` calls genuine all-or-nothing semantics — including across two buckets on the same
cluster (see [Multiple buckets and clusters](configuration.md#multiple-buckets-and-clusters)).

## Basic usage

```
await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.Majority);

context.Blogs.Add(new Blog { BlogId = 1, Url = "http://example.com" });
await context.SaveChangesAsync();

await transaction.CommitAsync();
```

If you don't call `CommitAsync()` before the transaction is disposed (or you call
`RollbackAsync()` explicitly), none of the writes made through `SaveChangesAsync()` while the
transaction was open are persisted.

> [!NOTE]
> A read (e.g. `FindAsync`) issued immediately after `CommitAsync()` can momentarily still miss
> the just-committed document — KV reads aren't governed by the `RequestPlus` query-scan-consistency
> option (that only affects N1QL queries), so there's a brief window before a point read reflects
> the commit. If you need to read back a value you just wrote inside a transaction, poll briefly
> or re-query rather than assuming an immediate KV read is guaranteed consistent.

## Durability level

`BeginCouchbaseTransactionAsync` requires a `Couchbase.KeyValue.DurabilityLevel`, which controls
how durably the transaction's writes must be persisted across the cluster before `CommitAsync()`
returns:

| `DurabilityLevel`             | Meaning                                                        |
|--------------------------------|-----------------------------------------------------------------|
| `None`                          | No durability requirement. Fastest; appropriate for a single-node dev/test cluster, where there's no replica to persist to. |
| `Majority`                      | Must be replicated to a majority of configured replicas.        |
| `MajorityAndPersistToActive`     | Majority replication, plus persisted to disk on the active node. |
| `PersistToMajority`              | Persisted to disk on a majority of replicas.                    |

An `IsolationLevel` overload also exists (`BeginCouchbaseTransactionAsync(durabilityLevel, isolationLevel, cancellationToken)`); it's informational only for Couchbase and doesn't change transaction behavior.

## Cross-bucket transactions

A transaction spans every bucket a `DbContext` writes to during it — including a single context
[mapping different entities to different buckets](configuration.md#one-context-spanning-multiple-buckets).
A commit persists the writes to every bucket together; a rollback, or a failure that prevents
commit, leaves every bucket unchanged — not just the one where the failure occurred.

```
public class SpanningContext(DbContextOptions<SpanningContext> options) : DbContext(options)
{
    public DbSet<WidgetA> WidgetsA { get; set; } = null!;
    public DbSet<WidgetB> WidgetsB { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WidgetA>().ToCouchbaseCollection("default", "isolation", "widget");
        modelBuilder.Entity<WidgetB>().ToCouchbaseCollection("secondary", "isolation", "widget");
    }
}

await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.Majority);

context.Add(new WidgetA { Id = 1, Name = "a" });   // bucket "default"
context.Add(new WidgetB { Id = 1, Name = "b" });   // bucket "secondary"
await context.SaveChangesAsync();

await transaction.CommitAsync();   // both buckets commit together, or neither does
```

If the commit fails partway through — for example, one bucket's write conflicts with an
already-existing document — `CommitAsync()` throws
`Couchbase.Client.Transactions.Error.TransactionFailedException`, and the write already staged to
the *other* bucket is rolled back too. Both buckets must share one physical cluster; a
transaction cannot span buckets on different clusters (use `ServiceKey` with a separate context
per cluster instead — see [Multiple clusters](configuration.md#multiple-clusters)).

## Other transaction helpers

- **`transaction.GetCommittedCount()`** — an extension method on `IDbContextTransaction` returning
  the number of operations committed by this transaction, or `0` if it isn't a Couchbase
  transaction.
- **`context.Database.GetCouchbaseClient()`/`GetCouchbaseClientAsync()`** — resolve the underlying
  `ICluster` this context's connection is using, for scenarios that need direct SDK access
  alongside EF Core.
