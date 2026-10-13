using Couchbase.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// A test store backed by one Couchbase scope. EF's spec fixtures name each store after the
/// database they would create on a relational engine; here that name becomes the scope.
/// </summary>
public class CouchbaseTestStore : TestStore
{
    public CouchbaseTestStore(string name, bool shared) : base(name, shared)
    {
    }

    public static CouchbaseTestStore GetOrCreate(string name) => new(name, shared: true);

    public static CouchbaseTestStore Create(string name) => new(name, shared: false);

    protected override async Task InitializeAsync(
        Func<DbContext> createContext,
        Func<DbContext, Task>? seed,
        Func<DbContext, Task>? clean)
    {
        await using var context = createContext();
        if (clean != null)
        {
            await clean(context);
        }

        await CleanAsync(context);

        if (seed != null)
        {
            await seed(context);
        }
    }

    /// <summary>
    /// Leaves the store's scope empty but with its collections created. Deliberately does not use
    /// <c>Database.EnsureDeleted()</c>: the provider implements that by dropping the whole bucket,
    /// which would take every other scope in the shared test bucket with it.
    /// </summary>
    public override async Task CleanAsync(DbContext context)
    {
        await DropScopeAsync();
        await context.Database.EnsureCreatedAsync();
    }

    private async Task DropScopeAsync()
    {
        var cluster = await Cluster.ConnectAsync(
            CouchbaseTestEnvironment.ConnectionString,
            CouchbaseTestEnvironment.Username,
            CouchbaseTestEnvironment.Password);
        try
        {
            var bucket = await cluster.BucketAsync(CouchbaseTestEnvironment.Bucket);
            await bucket.Collections.DropScopeAsync(Name);
        }
        catch (ScopeNotFoundException)
        {
            // Nothing to drop on a first run.
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => builder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(CouchbaseTestEnvironment.ConnectionString)
                .WithPasswordAuthentication(CouchbaseTestEnvironment.Username, CouchbaseTestEnvironment.Password),
            o =>
            {
                o.Bucket = CouchbaseTestEnvironment.Bucket;
                o.Scope = Name;
                o.AutoCreateScopes = true;
                // Spec tests seed and then immediately query through N1QL; wait for the index.
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            }).ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
}
