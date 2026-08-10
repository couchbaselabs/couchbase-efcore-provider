# Optimistic concurrency and document metadata

Couchbase's KV API tracks a CAS (compare-and-swap) value on every document — an opaque value that
changes on every mutation, the Couchbase equivalent of a SQL rowversion. N1QL exposes this and
other per-document metadata through the `META()` function
([reference](https://docs.couchbase.com/server/current/n1ql/n1ql-language-reference/indexing-meta-info.html)).
The provider maps `META()` fields onto ordinary shadow properties via `[CouchbaseMeta]`/
`HasCouchbaseMeta`, most importantly CAS as an EF Core optimistic-concurrency token.

Without a CAS-backed concurrency token, `SaveChangesAsync` performs an unconditional write —
two concurrent updates to the same document silently overwrite each other, with no error and no
way for either caller to detect it. Opting into CAS-based concurrency closes that gap.

## CAS as a concurrency token

Add a `ulong` property, mark it with `[CouchbaseMeta(CouchbaseMetaField.Cas)]` **and** EF Core's
own `.IsConcurrencyToken()` — both are required together, so calling `.IsConcurrencyToken()` on
an unrelated property never silently starts sending CAS checks:

```
public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    [CouchbaseMeta(CouchbaseMetaField.Cas)]
    public ulong Cas { get; set; }
}
```

```
modelBuilder.Entity<Order>()
    .Property(e => e.Cas)
    .IsConcurrencyToken();
```

The property is populated automatically — never set it yourself:

* After `SaveChangesAsync` inserts or updates the entity, `Cas` is refreshed with the document's
  new CAS, so a later `SaveChangesAsync` against the same tracked instance checks against the
  correct value.
* Any query that reads the entity also reads its current CAS via `META(alias).cas`.

When `SaveChangesAsync` sends an update or delete for an entity with a CAS-backed concurrency
token, it includes the CAS value it last read. If the document was modified or deleted by another
process in the meantime, Couchbase's compare-and-swap check fails and the provider throws EF
Core's own `DbUpdateConcurrencyException` — the same exception type and handling pattern (reload,
merge, retry) EF Core applications already use for any other provider:

```
try
{
    await context.SaveChangesAsync();
}
catch (DbUpdateConcurrencyException)
{
    // Reload the entity (and its Cas) and retry, or surface the conflict to the caller.
}
```

## Reading other META() fields

`[CouchbaseMeta(CouchbaseMetaField.Id)]` (a `string` property),
`[CouchbaseMeta(CouchbaseMetaField.Expiration)]` (a `long` property, Unix epoch seconds — `0`
means no expiration), `[CouchbaseMeta(CouchbaseMetaField.Flags)]` (a `uint` property — an opaque
value the SDK's KV layer uses to record the document's datatype), and
`[CouchbaseMeta(CouchbaseMetaField.Type)]` (a `string` property — e.g. `"json"`) all work the same
way, but are read-only: the provider has no API for setting a document's key, TTL, flags, or type
on write.

```
public class Order
{
    public int Id { get; set; }

    [CouchbaseMeta(CouchbaseMetaField.Id)]
    public string DocumentId { get; set; } = string.Empty;

    [CouchbaseMeta(CouchbaseMetaField.Expiration)]
    public long ExpiresAt { get; set; }
}
```

A `[CouchbaseMeta]` property must be the exact CLR type its field requires (`ulong` for `Cas`,
`string` for `Id`/`Type`, `long` for `Expiration`, `uint` for `Flags`) — applying it to any other
type throws `InvalidOperationException` at model-build time, and the fluent
`HasCouchbaseMeta(...)` form throws the same way.

> [!WARNING]
> **Known Couchbase Server limitation:** don't put both `[CouchbaseMeta(CouchbaseMetaField.Flags)]`
> and `[CouchbaseMeta(CouchbaseMetaField.Expiration)]` on the same queried entity. Projecting
> `META(alias).flags` together with `META(alias).expiration` in one `SELECT` makes the Couchbase
> Server query engine itself return `0` for `flags`, regardless of the document's real value —
> confirmed by issuing the exact SQL directly via the SDK and observing the wrong value already
> present in the raw N1QL response, so this is not something this provider's SQL generation or
> materialization causes or can work around. `Flags` reads back correctly alone, or combined with
> `Cas`/`Id`/`Type` — only the combination with `Expiration` is affected.

Not supported: `META().xattrs` (extended attributes).

## Read-your-own-writes (`ConsistentWith`)

By default, queries use `NotBounded` scan consistency (see [Limitations — Query scan
consistency](limitations.md#querying-and-consistency)): a document written a moment ago may not
yet be visible to a subsequent SQL++ query, because secondary (GSI) indexes update
asynchronously. Setting `ScanConsistency = RequestPlus` on the options builder fixes this for
*every* query on that context, but that's an all-or-nothing, context-wide switch — every query
pays the extra latency of waiting for the index to catch up, even ones that don't need read-your-
own-writes at all.

`ConsistentWith` scopes that guarantee to a specific write instead: it makes one query (or one
`FromSqlRaw`/`FromSql`/ADO.NET command) wait only until the index reflects a *specific* prior
mutation, not the whole collection's latest state. This is EF Core's provider-level surface over
the Couchbase SDK's own `MutationState`
([reference](https://docs.couchbase.com/dotnet-sdk/current/concept-docs/durability-replication-failure-considerations.html#at_plus)),
which the SDK internally represents as a set of `MutationToken`s (one per document write) and
resolves against by forcing `AT_PLUS` scan consistency.

`SaveChangesAsync` automatically accumulates a `MutationState` on the `DbContext` from every
document it writes — there's nothing to opt into on the write side:

```
context.Add(new Order { CustomerName = "Ada" });
await context.SaveChangesAsync();

var mutationState = context.Database.GetMutationState();
```

Pass that `MutationState` to `ConsistentWith` on the read side, across any of the three query
execution paths:

```
// LINQ -- ConsistentWith must be the LAST operator in the chain (see below).
var order = await context.Orders
    .Where(o => o.CustomerName == "Ada")
    .ConsistentWith(mutationState)
    .SingleOrDefaultAsync();

// FromSqlRaw / FromSql
var orders = await context.Orders
    .FromSqlRaw("SELECT o.* FROM `bucket`.`scope`.`orders` AS o WHERE o.customerName = {0}", "Ada")
    .ConsistentWith(mutationState)
    .ToListAsync();

// Raw ADO.NET
using var command = (CouchbaseCommand)connection.CreateCommand();
command.ConsistentWith = mutationState;
command.CommandText = "SELECT o.* FROM `bucket`.`scope`.`orders` AS o WHERE o.customerName = $name";
```

`context.Database.ClearMutationState()` resets the accumulated state (e.g. between logically
unrelated units of work sharing one long-lived context).

> [!WARNING]
> On the LINQ path, `ConsistentWith(...)` must be the **last** operator in the query — composing
> any further LINQ operator after it (`.Where(...)`, `.OrderBy(...)`, another `.Select(...)`, etc.)
> throws `InvalidOperationException` at query-translation time (a clear, immediate failure, not a
> silently-ignored hint). Apply every other operator first, then call `.ConsistentWith(...)` last:
> `context.Orders.Where(...).OrderBy(...).ConsistentWith(mutationState)`, not the reverse.

`MutationState` only ever grows narrower guarantees than `RequestPlus` — it says "wait for *these*
writes to be indexed," not "wait for the whole collection to be caught up" — so prefer it over a
context-wide `RequestPlus` whenever the read-after-write need is scoped to a specific prior write
rather than the collection as a whole.
