# Configuring the EF Core Couchbase DB Provider

The EF Core Couchbase DB provider ties directly into [.NET Core Dependency Injection (DI)](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) via the [Couchbase Dependency Injection Library](https://docs.couchbase.com/dotnet-sdk/current/howtos/managing-connections.html#connection-di).

## Configuring using IServiceCollection
For most applications dependencies are injected into the .NET Service Container during application startup, often in the class file that contains the `Main()` method:

```
builder.Services.AddDbContext<SchoolContext>(options=> options
    .UseCouchbase(new ClusterOptions()
    .WithCredentials("Administrator", "password")
    .WithConnectionString("couchbase://localhost"),
        couchbaseDbContextOptions =>
        {
            couchbaseDbContextOptions.Bucket = "universities";
            couchbaseDbContextOptions.Scope = "contoso";
        }));
```

In this example from the [Contoso University Sample application](https://github.com/couchbaselabs/couchbase-efcore-provider/tree/main/samples/ContosoUniversity), the EF Core Couchbase DB Provider is configured in the [Program.cs](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/samples/ContosoUniversity/Program.cs) file. This accomplishes two things: 1) inject the dependency on the Couchbase SDK and 2) inject the dependency on the EF Core Couchbase Provider into the application for the `SchoolContext`, which is a `DbContext`.

## Configuring using DbContext.OnConfiguring
The EF Core Couchbase DB Provider can also be configured on a per DbContext level. To do this, override the DbContext.OnConfiguring method and add inject the Couchbase SDK and Provider:

```
protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        using var loggingFactory = LoggerFactory.Create(builder => builder.AddConsole());
        options.UseCouchbase(new ClusterOptions()
                .WithCredentials("Administrator", "password")
                .WithConnectionString("couchbase://localhost")
                .WithLogging(loggingFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "Blogging";
                couchbaseDbContextOptions.Scope = "MyBlog";
            });
    }
```

You can see an example of this in the [CouchbaseGettingStarted](https://github.com/couchbaselabs/couchbase-efcore-provider/tree/main/samples/CouchbaseGettingStarted) example on [Model.cs](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/samples/CouchbaseGettingStarted/Model.cs#L13) class.

## Couchbase SDK options
Configuring the SDK is largely the same for EF Core Couchbase DB Provider. The SDK's configuration is handled by the ClusterOptions.cs class. More details of the ClusterOptions class can be found in the [main SDK documentation](https://docs.couchbase.com/dotnet-sdk/current/ref/client-settings.html).

## EF Core Couchbase DB Provider options
The [CouchbaseDbContextOptionsBuilder](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/src/Couchbase.EntityFrameworkCore/Infrastructure/CouchbaseDbContextOptionsBuilder.cs) handles configuration options specific to the provider, set in the `couchbaseDbContextOptions` callback passed to `UseCouchbase`/`AddCouchbase<TContext>`:

| Option | Default | Description |
|---|---|---|
| `Bucket` | *(required)* | The bucket this context targets by default. Individual entities can target a different bucket — see [One context spanning multiple buckets](#one-context-spanning-multiple-buckets). |
| `Scope` | *(required)* | The scope this context targets by default. |
| `ScanConsistency` | `NotBounded` | N1QL scan consistency for LINQ/`FromSql`/ADO.NET queries. Set `RequestPlus` for read-after-write consistency at the cost of higher query latency. See [Limitations](limitations.md) for the full explanation. |
| `AutoCreateScopes` | `false` | Whether `EnsureCreatedAsync` automatically creates non-default scopes referenced by entity mappings. When `false`, collections mapped to a non-default scope are skipped (with a warning) instead of created. |
| `AutoCreateIndexes` | `false` | Whether `EnsureCreatedAsync` automatically creates a primary index on every collection it creates or already owns, waiting for each index to come online before returning. Does not create secondary indexes — see [Limitations](limitations.md#schema-management-and-migrations). |
| `FieldNamingPolicy` | `JsonNamingPolicy.CamelCase` | Controls how CLR navigation names are converted to JSON field names when reading/writing `OwnsMany` embedded collections. Set to `null` to use the CLR name verbatim (PascalCase), or supply a different policy such as `JsonNamingPolicy.SnakeCaseLower`. |
| `SerializerOptions` | `null` (uses `JsonSerializerDefaults.Web`) | `JsonSerializerOptions` used when deserializing scalar values inside `OwnsMany` collections. Supply a custom instance to match a non-default serializer configured on the Couchbase SDK (custom converters, different enum handling, etc.). |
| `DateTimeFormat` | `"yyyy-MM-ddTHH:mm:ss.FFFK"` | The .NET custom `DateTime` format string this provider assumes when generating or comparing against `DateTime` string values in SQL++ — used by the LINQ `DateTime` function translators (`.Date`, `.Now`, `.UtcNow`, `.Today`) and for inline `DateTime` literals. See [DateTime string format](#datetime-string-format) below. |
| `ServiceKey` | `null` | Selects which application-registered, keyed Couchbase cluster this context uses — see [Multiple clusters](#multiple-clusters). |

> [!NOTE]
> `FieldNamingPolicy` only controls casing for `OwnsMany` embedded collection fields — it has no
> effect on top-level entity property casing in generated SQL++ queries. That's a separate concern
> handled by [EFCore.NamingConventions](#controlling-querying-casing). If you change one, make sure
> it still matches the other (and your actual document casing), or fields will silently come back
> with default values instead of an error — see [Controlling Querying Casing](#controlling-querying-casing).

### DateTime string format

N1QL has no native date type — a `DateTime` is stored as a plain JSON string, so nothing stops
different data from using a different string convention (a different precision, a date-only value,
or data written by another system entirely). The `DateTimeFormat` option tells the provider which
convention your data actually uses, so `.Date`/`.Now`/`.UtcNow`/`.Today` comparisons and generated
`DateTime` literals stay correct instead of assuming one hardcoded format.

`DateTimeFormat` is a .NET custom `DateTime` format string. The default,
`"yyyy-MM-ddTHH:mm:ss.FFFK"`, matches this provider's own default `DateTime` serialization
(millisecond precision, `Z` for UTC or a real offset otherwise — e.g. `2026-03-14T09:26:53.123Z`).
Only the following tokens are supported (the ISO-8601-relevant subset — internally converted to
the equivalent [Go reference-time](https://pkg.go.dev/time#pkg-constants) layout N1QL's date
functions expect):

| Token | Meaning |
|---|---|
| `yyyy` | 4-digit year |
| `MM` | 2-digit month |
| `dd` | 2-digit day |
| `HH` | 2-digit hour (24-hour) |
| `mm` | 2-digit minute |
| `ss` | 2-digit second |
| `f` (repeated 1–7 times) | Fixed-width fractional seconds |
| `F` (repeated 1–7 times) | Trimmed fractional seconds (dropped, along with the decimal point, when the value is exactly zero) |
| `K` | `Z` for UTC, or a real `+hh:mm`/`-hh:mm` offset |
| `T` | Literal `T` — not a reserved .NET specifier, so it needs no quoting (unlike other letters) |
| non-letter characters (`-`, `:`, `.`, space, etc.) | Passed through as literal separators |
| `'...'` / `"..."` | A quoted literal string, copied through verbatim (needed to use a letter — other than `T` — as a literal, e.g. `'Z'`) |
| `\x` | Escapes the next character `x` as a literal, usable inside or outside a quoted section |

Any other bare letter (`tt`, `ddd`, `zzz`, 12-hour `hh`, an un-quoted `Z`, etc.) throws an
`ArgumentException` at configuration time (when `DateTimeFormat` is set) naming the unsupported
token, rather than failing later with a confusing SQL++ error. Configure it alongside your other
provider options:

```csharp
couchbaseDbContextOptions.DateTimeFormat = "yyyy-MM-dd"; // date-only convention
```

This is unrelated to `FieldNamingPolicy` — `DateTimeFormat` controls how `DateTime` *values* are
compared/generated in SQL++, while `FieldNamingPolicy` controls JSON *field name* casing.

#### Per-property override

`DateTimeFormat` applies to every `DateTime` property in the context by default, but a single
property can use a different convention — via the `[DateTimeFormat]` attribute or the
`HasDateTimeFormat` fluent API — without affecting any other property:

```csharp
public class Order
{
    public int Id { get; set; }
    public DateTime Placed { get; set; }             // uses the context-wide DateTimeFormat

    [DateTimeFormat("yyyy-MM-dd")]
    public DateTime ShipDate { get; set; }            // date-only, independent of Placed
}
```

```csharp
// Equivalent fluent form, e.g. if you'd rather not put attributes on the entity:
modelBuilder.Entity<Order>()
    .Property(o => o.ShipDate)
    .HasDateTimeFormat("yyyy-MM-dd");
```

The override only applies to the `.Date` member translator and inline `DateTime` literals for
that specific property — the static `DateTime.Now`/`.UtcNow`/`.Today` translators have no
associated property to read an override from, so they always use the context-wide default even
in a query that also touches an overridden property.

## Multiple buckets and clusters

By default a `DbContext` targets a single configured bucket (and scope), and every entity is
stored there. You can either use **one `DbContext` per bucket**, or map **individual entities to
different buckets on the same cluster** within a single context (see
[One context spanning multiple buckets](#one-context-spanning-multiple-buckets) below).

For the context-per-bucket approach, each context is registered independently and targets its own
bucket:

```csharp
builder.Services.AddCouchbase<OrdersContext>(clusterOptions,
    o => { o.Bucket = "orders"; o.Scope = "sales"; });

builder.Services.AddCouchbase<UsersContext>(clusterOptions,
    o => { o.Bucket = "users";  o.Scope = "identity"; });
```

### Sharing a single cluster (recommended)

Couchbase recommends a **single `Cluster` object per application**, cached and reused — one
cluster can open many buckets. To have your contexts share one cluster, register the Couchbase
SDK in your application's DI container (via the
[Couchbase DI library](https://docs.couchbase.com/dotnet-sdk/current/howtos/managing-connections.html#connection-di)),
and the provider will reuse it for every context bound to it:

```csharp
// One shared cluster for the whole application.
builder.Services.AddCouchbase(o =>
{
    o.ConnectionString = "couchbase://localhost";
    o.WithCredentials("Administrator", "password");
});

// Both contexts reuse the shared cluster, each targeting its own bucket.
builder.Services.AddCouchbase<OrdersContext>(clusterOptions, o => { o.Bucket = "orders"; o.Scope = "sales"; });
builder.Services.AddCouchbase<UsersContext>(clusterOptions,  o => { o.Bucket = "users";  o.Scope = "identity"; });
```

If no cluster is registered in application DI, each context creates and owns its own cluster
(the original behavior). Cluster sharing only applies when contexts are registered through
`AddCouchbase<TContext>` — the application must register the cluster in DI for it to be shared.

### Multiple clusters

When an application must talk to **more than one physical Couchbase Server cluster**, register a
**keyed** cluster per server and point each context at the right one with `ServiceKey`:

```csharp
builder.Services.AddKeyedCouchbase("east", o =>
{
    o.ConnectionString = "couchbase://east.example.com";
    o.WithCredentials("Administrator", "password");
});
builder.Services.AddKeyedCouchbase("west", o =>
{
    o.ConnectionString = "couchbase://west.example.com";
    o.WithCredentials("Administrator", "password");
});

builder.Services.AddCouchbase<EastContext>(eastClusterOptions,
    o => { o.ServiceKey = "east"; o.Bucket = "orders"; o.Scope = "sales"; });
builder.Services.AddCouchbase<WestContext>(westClusterOptions,
    o => { o.ServiceKey = "west"; o.Bucket = "orders"; o.Scope = "sales"; });
```

`ServiceKey` selects which keyed cluster a context binds to. If a `ServiceKey` is set but no
matching keyed cluster was registered, configuration fails with a clear error.

### One context spanning multiple buckets

A single `DbContext` can map different entities to different buckets, as long as those buckets
live on the **same cluster** (same connection string). Give an entity an explicit keyspace with
the three-argument `ToCouchbaseCollection(bucket, scope, collection)`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Order is stored in the "orders" bucket, Customer in the "users" bucket.
    modelBuilder.Entity<Order>().ToCouchbaseCollection("orders", "sales", "order");
    modelBuilder.Entity<Customer>().ToCouchbaseCollection("users", "identity", "customer");

    // Entities without an explicit bucket fall back to the context's configured Bucket/Scope.
    modelBuilder.ConfigureToCouchbase(this);
}
```

Or with the attribute form:

```csharp
[CouchbaseKeyspace("users", "identity", "customer")] // bucket, scope, collection
public class Customer { /* ... */ }
```

Reads, `Find`, queries, and `SaveChanges` all resolve each entity's own bucket automatically, and
`EnsureCreated` creates the scopes/collections in each bucket (you must still create a query index
on each collection yourself — see [Limitations](limitations.md#schema-management-and-migrations)).
N1QL queries and multi-document transactions work across these buckets because they share one
cluster — see [Transactions](transactions.md) for the full cross-bucket commit/rollback guarantee
and example.

> [!NOTE]
> Buckets mapped within one context must be on the **same** physical cluster. To reach buckets on
> **different** clusters, use a separate context per cluster with `ServiceKey` (above) — a single
> query or transaction cannot span clusters. See [Limitations](limitations.md).

## Controlling Querying Casing
SQL++ is based off JSON, thus is case sensitive both from the Query perspective and the query output. What this means is that your Keyspaces (Bucket, Scopes and Collections), the SQL++ query casing and your entities must have consistent casing.

You can control the casing of your entities using standard `NewtonSoft.JsonProperty` and or `System.Text.Json.JsonPropertyName` attributes, by the property or field names themselves and by using the `Column` attribute.

The generated SQL++ casing can be controlled via the [EFCore.NamingConventions](https://www.nuget.org/packages/EFCore.NamingConventions) library:
```
dotnet add package EFCore.NamingConventions --version 10.0.1
```
Which is added as part of configuration of the EF Core Couchbase DB Provider:
```
optionsBuilder.UseCouchbase(_clusterOptions, couchbaseDbContextOptions =>
{
    couchbaseDbContextOptions.Bucket = "travel-sample";
    couchbaseDbContextOptions.Scope = "inventory";
});
optionsBuilder.UseCamelCaseNamingConvention();
```

> [!NOTE]
> This is a separate mechanism from the [`FieldNamingPolicy`](#ef-core-couchbase-db-provider-options)
> option — `EFCore.NamingConventions` governs top-level entity property casing in generated SQL++
> queries, while `FieldNamingPolicy` governs field casing inside `OwnsMany` embedded collections
> only. Keep both consistent with your actual document casing.

[Documentation](https://github.com/efcore/EFCore.NamingConventions) for EFCore.Naming Conventions is located on the Github repo. Of interest is the section on [supported naming conventions](https://github.com/efcore/EFCore.NamingConventions?tab=readme-ov-file#supported-naming-conventions).
