using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Multi-bucket DI coverage (Scope A — context-per-bucket): two DbContexts share a cluster,
/// scope, and collection name, differing only by bucket. Writing the same key to each must
/// not collide — each context resolves its own bucket and reads back only its own document.
/// This is the strict isolation proof that DependencyInjectionTests (which uses two different
/// schemas) does not provide.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class MultiBucketDependencyInjectionTests(BloggingFixture fixture)
{
    [Fact]
    public async Task TwoContexts_OnDifferentBuckets_AreIsolated()
    {
        var services = new ServiceCollection();
        services.AddCouchbase<PrimaryWidgetContext>(
            NewClusterOptions(),
            o => ConfigureBucket(o, "default"),
            o => { o.UseCamelCaseNamingConvention(); SuppressManyProvidersWarning(o); });
        services.AddCouchbase<SecondaryWidgetContext>(
            NewClusterOptions(),
            o => ConfigureBucket(o, "secondary"),
            o => { o.UseCamelCaseNamingConvention(); SuppressManyProvidersWarning(o); });

        await using var provider = services.BuildServiceProvider();

        // Write the same key (WidgetId = 1) to each bucket with a distinct value.
        await using (var scope = provider.CreateAsyncScope())
        {
            var primary = scope.ServiceProvider.GetRequiredService<PrimaryWidgetContext>();
            var secondary = scope.ServiceProvider.GetRequiredService<SecondaryWidgetContext>();

            await primary.Database.EnsureCreatedAsync();
            await secondary.Database.EnsureCreatedAsync();

            primary.Update(new Widget { WidgetId = 1, Name = "from-default" });
            await primary.SaveChangesAsync();

            secondary.Update(new Widget { WidgetId = 1, Name = "from-secondary" });
            await secondary.SaveChangesAsync();
        }

        // Read back in fresh contexts (no identity-map carryover).
        await using (var scope = provider.CreateAsyncScope())
        {
            var primary = scope.ServiceProvider.GetRequiredService<PrimaryWidgetContext>();
            var secondary = scope.ServiceProvider.GetRequiredService<SecondaryWidgetContext>();

            var fromPrimary = await primary.Widgets.FindAsync(1);
            var fromSecondary = await secondary.Widgets.FindAsync(1);

            Assert.NotNull(fromPrimary);
            Assert.NotNull(fromSecondary);
            // Same key, same scope/collection, different bucket → no cross-talk.
            Assert.Equal("from-default", fromPrimary!.Name);
            Assert.Equal("from-secondary", fromSecondary!.Name);
        }
    }

    [Fact]
    public void ContextsWithDifferentConnectionStrings_ResolveDistinctClusterProviders()
    {
        // Multi-cluster isolation: two contexts registered with different connection strings
        // must each get their own Couchbase cluster/bucket provider — no singleton bleed where
        // the second context silently reuses the first cluster. We assert provider identity
        // only (resolving IBucketProvider does not open a connection), so the second connection
        // string need not be reachable — avoiding a heavyweight second Couchbase container.
        var services = new ServiceCollection();
        services.AddCouchbase<PrimaryWidgetContext>(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password),
            o => ConfigureBucket(o, "default"),
            SuppressManyProvidersWarning);
        services.AddCouchbase<SecondaryWidgetContext>(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString("couchbase://cluster-b.invalid")
                .WithCredentials("user", "password"),
            o => ConfigureBucket(o, "default"),
            SuppressManyProvidersWarning);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var primary = scope.ServiceProvider.GetRequiredService<PrimaryWidgetContext>();
        var secondary = scope.ServiceProvider.GetRequiredService<SecondaryWidgetContext>();

        var primaryBucketProvider = primary.GetService<IBucketProvider>();
        var secondaryBucketProvider = secondary.GetService<IBucketProvider>();

        Assert.NotNull(primaryBucketProvider);
        Assert.NotNull(secondaryBucketProvider);
        Assert.NotSame(primaryBucketProvider, secondaryBucketProvider);
    }

    [Fact]
    public async Task AppRegisteredCluster_IsSharedAcrossContextsAndBuckets()
    {
        // When the application registers its own cluster in DI, every context bound to it (across
        // buckets) reuses the SAME ICluster — one Cluster per server, per Couchbase guidance —
        // instead of each context spinning up its own.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCouchbase(o =>
        {
            o.ConnectionString = fixture.Host;
            o.WithCredentials(fixture.Username, fixture.Password);
        });
        // Distinct scope so this test gets its own EF internal service-provider cache key
        // (EF caches internal providers process-wide by connection string/bucket/scope/key).
        services.AddCouchbase<PrimaryWidgetContext>(NewClusterOptions(),
            o => ConfigureBucket(o, "default", "clustershared"),
            o => { o.UseCamelCaseNamingConvention(); SuppressManyProvidersWarning(o); });
        services.AddCouchbase<SecondaryWidgetContext>(NewClusterOptions(),
            o => ConfigureBucket(o, "secondary", "clustershared"),
            o => { o.UseCamelCaseNamingConvention(); SuppressManyProvidersWarning(o); });

        await using var provider = services.BuildServiceProvider();

        var appCluster = await provider.GetRequiredService<IClusterProvider>().GetClusterAsync();

        await using var scope = provider.CreateAsyncScope();
        var primary = scope.ServiceProvider.GetRequiredService<PrimaryWidgetContext>();
        var secondary = scope.ServiceProvider.GetRequiredService<SecondaryWidgetContext>();

        var primaryCluster = await primary.GetService<IClusterProvider>().GetClusterAsync();
        var secondaryCluster = await secondary.GetService<IClusterProvider>().GetClusterAsync();

        Assert.Same(appCluster, primaryCluster);
        Assert.Same(appCluster, secondaryCluster);
    }

    [Fact]
    public async Task ServiceKey_BindsEachContextToItsKeyedCluster()
    {
        // Multiple physical clusters: each context selects its cluster by ServiceKey, and contexts
        // with different keys resolve distinct ICluster instances.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedCouchbase("clusterA", o =>
        {
            o.ConnectionString = fixture.Host;
            o.WithCredentials(fixture.Username, fixture.Password);
        });
        services.AddKeyedCouchbase("clusterB", o =>
        {
            o.ConnectionString = fixture.Host;
            o.WithCredentials(fixture.Username, fixture.Password);
        });
        services.AddCouchbase<PrimaryWidgetContext>(NewClusterOptions(),
            o => { ConfigureBucket(o, "default"); o.ServiceKey = "clusterA"; },
            o => { o.UseCamelCaseNamingConvention(); SuppressManyProvidersWarning(o); });
        services.AddCouchbase<SecondaryWidgetContext>(NewClusterOptions(),
            o => { ConfigureBucket(o, "secondary"); o.ServiceKey = "clusterB"; },
            o => { o.UseCamelCaseNamingConvention(); SuppressManyProvidersWarning(o); });

        await using var provider = services.BuildServiceProvider();

        var clusterA = await provider.GetRequiredKeyedService<IClusterProvider>("clusterA").GetClusterAsync();
        var clusterB = await provider.GetRequiredKeyedService<IClusterProvider>("clusterB").GetClusterAsync();

        await using var scope = provider.CreateAsyncScope();
        var primary = scope.ServiceProvider.GetRequiredService<PrimaryWidgetContext>();
        var secondary = scope.ServiceProvider.GetRequiredService<SecondaryWidgetContext>();

        Assert.Same(clusterA, await primary.GetService<IClusterProvider>().GetClusterAsync());
        Assert.Same(clusterB, await secondary.GetService<IClusterProvider>().GetClusterAsync());
        Assert.NotSame(clusterA, clusterB);
    }

    private global::Couchbase.ClusterOptions NewClusterOptions()
        => new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password);

    private static void ConfigureBucket(ICouchbaseDbContextOptionsBuilder options, string bucket, string scope = "isolation")
    {
        options.Bucket = bucket;
        options.Scope = scope;
        // Read-after-write consistency so the FindAsync below sees the just-written document.
        options.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
    }

    // This class deliberately registers many distinct bucket/scope/cluster/ServiceKey
    // combinations across its tests to prove real DI isolation — legitimately crossing EF Core's
    // own ">20 internal service providers" heuristic across the full test suite. Suppress the
    // resulting warning per-context; it's the officially documented way to acknowledge it's expected.
    private static void SuppressManyProvidersWarning(DbContextOptionsBuilder options)
        => options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

    public class Widget
    {
        public int WidgetId { get; set; }
        public string Name { get; set; } = null!;
    }

    public abstract class WidgetContextBase(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Widget> Widgets { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>().ToCouchbaseCollection(this, "widget");
            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }

    public sealed class PrimaryWidgetContext(DbContextOptions<PrimaryWidgetContext> options)
        : WidgetContextBase(options);

    public sealed class SecondaryWidgetContext(DbContextOptions<SecondaryWidgetContext> options)
        : WidgetContextBase(options);
}
