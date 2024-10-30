# Configuring the EF Core Couchbase DB Provider

The EF Core Couchbase DB provider ties directly into [.NET Core Dependency Injection (DI)](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) via the [Couchbase Dependency Injection Library](https://docs.couchbase.com/dotnet-sdk/current/howtos/managing-connections.html#connection-di).

## Configuring using IServiceCollection
For most applications dependencies are injected into the .NET Service Container during application startup, often in the class file that contains the `Main()` method:

```
builder.Services.AddDbContext<SchoolContext>(options=> options
    .UseCouchbase<INamedBucketProvider>(new ClusterOptions()
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
        options.UseCouchbase<INamedBucketProvider>(new ClusterOptions()
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

## EF Core Couchbase DN Provider options
The [CouchbaseDbContextOptionsBuilder](https://github.com/couchbaselabs/couchbase-efcore-provider/blob/main/src/Couchbase.EntityFrameworkCore/Infrastructure/CouchbaseDbContextOptionsBuilder.cs) handles configuration options specific to the provider.
