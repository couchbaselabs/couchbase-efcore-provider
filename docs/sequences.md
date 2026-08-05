# Sequences and generated values

Beyond client-generated GUIDs, the provider supports server-side Couchbase sequences for
integer/long primary keys, configured either via the fluent `UseSequence` API or a
`[CouchbaseSequence]` attribute.

## UseSequence (fluent API)

The simplest form looks up a sequence of the given name in the bucket/scope the `DbContext` is
already configured for — unless the property's entity is itself mapped to a different bucket
(via `ToCouchbaseCollection(bucket, scope, collection)` or `[CouchbaseKeyspace]`), in which case
the sequence's **bucket** automatically follows that entity's actual bucket, both when it's
auto-created and when a value is generated at runtime. The sequence's **scope** does not follow
the entity the same way — it always defaults to the context's configured scope unless you pass
one explicitly (below).

```
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseSequence("order_seq");
```

To target a sequence in a different scope than the context's default, pass the scope explicitly:

```
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseSequence("analytics", "order_seq");
```

To control the sequence's starting value and increment (used when the sequence is auto-created —
see below), pass `CouchbaseSequenceOptions`:

```
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseSequence("order_seq", new CouchbaseSequenceOptions
    {
        StartWith = 1000,
        IncrementBy = 10
    });
```

`CouchbaseSequenceOptions` also supports `MaxValue`, `MinValue`, `Cycle` (restart when the limit
is reached; defaults to `false`), and `CacheSize` — these map directly to SQL++'s `CREATE
SEQUENCE` options.

## [CouchbaseSequence] attribute

The same configuration is available as an attribute directly on the property, which some prefer
for keeping ID-generation strategy visible on the model class itself:

```
public class Order
{
    [CouchbaseSequence("order_seq")]
    public long Id { get; set; }

    public string CustomerName { get; set; }
}
```

The attribute also has a `(scope, sequenceName)` constructor, and `StartWith`, `IncrementBy`,
`Cycle`, and `AutoCreate` properties mirroring `CouchbaseSequenceOptions`:

```
public class Order
{
    [CouchbaseSequence("order_seq", StartWith = 1000, IncrementBy = 10)]
    public long Id { get; set; }
}
```

## Auto-creation via EnsureCreatedAsync

By default (`AutoCreate = true`), calling `context.Database.EnsureCreatedAsync()` creates any
sequence configured on the DbContext-level scope that doesn't already exist, using the
configured `StartWith`/`IncrementBy`/etc. — equivalent to running:

```
CREATE SEQUENCE IF NOT EXISTS `bucket`.`scope`.`order_seq` START WITH 1000 INCREMENT BY 10 NO CYCLE
```

Sequences targeting a **non-default scope** (via `UseSequence(scope, ...)` or the attribute's
`Scope` property) are **not** auto-created, since that scope might not exist yet — a warning is
logged instead. Create the scope and sequence yourself in that case, or set `AutoCreate = false`
if you're managing the sequence's lifecycle entirely outside of `EnsureCreatedAsync`.

If the property's entity is mapped to a different bucket than the context's configured one, the
sequence is created (and, at runtime, queried via `NEXT VALUE FOR`) in that entity's actual
bucket, matching how collections and indexes already resolve per-entity — not the context's
configured bucket. Two sequences with the same name and scope in different buckets are distinct,
not a naming conflict, since a sequence's true identity is `bucket.scope.name`.

## Generated GUIDs

For distributed-ID scenarios where a server round-trip per insert isn't worth it, `UseGuid()`
and `UseGuidString()` generate a value client-side (via EF Core's built-in generator) instead of
using a server-side sequence:

```
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseGuid();
```

For a `string`-typed key that should hold a GUID's text representation, `UseGuidString(format)`
generates a new GUID and formats it as a string — `format` accepts the standard .NET GUID format
specifiers `"D"` (default, hyphenated), `"N"` (no hyphens), `"B"` (braces), or `"P"` (parentheses):

```
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseGuidString("N"); // No hyphens
```

`UseGuid()` requires the property to actually be typed `Guid` — calling it on a `string` property
throws `InvalidOperationException` at model-build time with a message pointing you to
`UseGuidString()` instead.
