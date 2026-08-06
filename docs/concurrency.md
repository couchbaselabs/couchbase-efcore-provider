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
