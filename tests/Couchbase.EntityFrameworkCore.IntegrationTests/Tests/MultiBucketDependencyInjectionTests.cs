using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
            o => o.UseCamelCaseNamingConvention());
        services.AddCouchbase<SecondaryWidgetContext>(
            NewClusterOptions(),
            o => ConfigureBucket(o, "secondary"),
            o => o.UseCamelCaseNamingConvention());

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

    private global::Couchbase.ClusterOptions NewClusterOptions()
        => new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password);

    private static void ConfigureBucket(ICouchbaseDbContextOptionsBuilder options, string bucket)
    {
        options.Bucket = bucket;
        options.Scope = "isolation";
        // Read-after-write consistency so the FindAsync below sees the just-written document.
        options.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
    }

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
