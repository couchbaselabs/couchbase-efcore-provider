# EF Core Couchbase DB Provider

This database provider allows Entity Framework Core to be used with Couchbase Database. The provider is maintained as part of the [Couchbase EFCore Project](https://github.com/couchbaselabs/couchbase-efcore-provider).

It is strongly recommended to familiarize yourself with the [Couchbase Database documentation](https://docs.couchbase.com/home/index.html) before reading this section. The EF Core Couchbase Db Provider, works
with [Couchbase Server](https://docs.couchbase.com/home/server.html) and [Couchbase Capella DBaaS](https://docs.couchbase.com/home/cloud.html).

## Install

Install the Couchbase.EntityFrameworkCore NuGet package.

### .NET Core CLI or Jet Brains Rider IDE
```
dotnet add package Couchbase.EntityFrameworkCore
```
### Visual Studio
```ps1
Install-Package Couchbase.EntityFrameworkCore
```

### Control casing
This is  required as the Couchbase defaults to Camel-Casing but EF Core defaults to Pascal-Casing for generated SQL:

```ps1
Install-Package EFCore.NamingConventions
```

Without this queries will likely return back empty results as the JSON is case sensitive.

## Get Started

> [!TIP]
> You can view this article's [sample on GitHub](https://github.com/couchbaselabs/couchbase-efcore-provider/tree/main/samples/ContosoUniversity)

As for other providers the first step is to call UseCouchbase:

```cs
protected override void OnConfiguring(DbContextOptionsBuilder options)
    => options.UseCouchbase(new ClusterOptions()
           .WithCredentials("Administrator", "password")
           .WithConnectionString("couchbase://localhost"),
                couchbaseDbContextOptions =>
                {
                    couchbaseDbContextOptions.Bucket = "OrdersDB";
                    couchbaseDbContextOptions.Scope = "_default";
                })
        .UseCamelCaseNamingConvention();
```

In this example Order is a simple entity with a reference to the [owned type](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities) StreetAddress.

```cs
public class Order
{
    public int Id { get; set; }
    public int? TrackingNumber { get; set; }
    public string PartitionKey { get; set; }
    public StreetAddress ShippingAddress { get; set; }
}

public class StreetAddress
{
    public string Street { get; set; }
    public string City { get; set; }
}
```

Saving and querying data follows the normal EF pattern:

```cs
using (var context = new OrderContext())
{
    await context.Database.EnsureDeletedAsync();
    await context.Database.EnsureCreatedAsync();

    context.Add(
        new Order
        {
            Id = 1, ShippingAddress = new StreetAddress { City = "London", Street = "221 B Baker St" }, PartitionKey = "1"
        });

    await context.SaveChangesAsync();
}

using (var context = new OrderContext())
{
    var order = await context.Orders.FirstAsync();
    Console.WriteLine($"First order will ship to: {order.ShippingAddress.Street}, {order.ShippingAddress.City}");
    Console.WriteLine();
}
```

* [The Contoso University web app code](https://github.com/couchbaselabs/couchbase-efcore-provider/tree/main/samples/ContosoUniversity)
* [Contoso University sample documentation](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/contoso-sample.md)
* [Getting started - _in more detail_](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/getting-started.md)

## Couchbase DB options
There exists options for both the [Couchbase SDK](https://docs.couchbase.com/dotnet-sdk/current/hello-world/start-using-sdk.html) which the Couchbase EF Core DB Provider uses and for the provider itself.
* [ClusterOptions](https://docs.couchbase.com/dotnet-sdk/current/ref/client-settings.html) settings
* Couchbase EF Core DB Provider settings

 # What works:
 * Basic projections/queries
 * Some SQL++ functions - COUNT, CONTAINS, etc
 * Basic CRUD and change tracking

 # What doesn't work
 * Eager Loading
 * Most all SQL++ functions
 * Value generation
 * META, RYOW, etc
 * Sync IO Methods
 * Lots...it's a WIP

# Documentation
* [Configuration](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/configuration.md)
* [Modeling](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/modeling.md)
* [Querying](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/Queries.md)
* [Saving Data](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/crud.md)
* [Getting started - In Depth](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/getting-started.md)
* [Contoso University - Sample](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/contoso-sample.md)
* [Couchbase EF Core limitations](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/docs/limitations.md)
