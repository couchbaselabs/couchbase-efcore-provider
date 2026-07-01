using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Multi-bucket single-context coverage (Scope B): a single DbContext maps different entity
/// types to different buckets on the same cluster. Writes in one SaveChanges fan out to both
/// buckets, queries and Find target the correct bucket, and the two buckets stay isolated even
/// when they share scope/collection names and primary-key values.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class MultiBucketSingleContextTests(BloggingFixture fixture)
{
    [Fact]
    public async Task SingleContext_SpanningTwoBuckets_WritesReadsAndQueriesEachBucket()
    {
        var services = new ServiceCollection();
        services.AddCouchbase<SpanningContext>(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password),
            o =>
            {
                // "default" is the configured (primary) bucket; Gizmo overrides to "secondary".
                o.Bucket = "default";
                o.Scope = "isolation";
                // Read-after-write consistency so the reads below see the just-written docs.
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            },
            o => o.UseCamelCaseNamingConvention());

        await using var provider = services.BuildServiceProvider();

        // Write the same PK (1) into both buckets with distinct values, in one SaveChanges.
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();

            // Exercises per-bucket EnsureCreated (creates isolation.widget in both buckets).
            await context.Database.EnsureCreatedAsync();

            context.Update(new Widget { Id = 1, Name = "widget-in-default" });
            context.Update(new Gizmo { Id = 1, Name = "gizmo-in-secondary" });
            await context.SaveChangesAsync();
        }

        // Read back in a fresh context (no identity-map carryover).
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();

            // KV path (Find) resolves each entity's own bucket.
            var widget = await context.Widgets.FindAsync(1);
            var gizmo = await context.Gizmos.FindAsync(1);

            Assert.NotNull(widget);
            Assert.NotNull(gizmo);
            // Same key, same scope/collection, different bucket → no cross-talk.
            Assert.Equal("widget-in-default", widget!.Name);
            Assert.Equal("gizmo-in-secondary", gizmo!.Name);

            // Query path (N1QL) targets the correct bucket in the FROM clause.
            var widgetsByName = await context.Widgets
                .Where(w => w.Name == "widget-in-default").ToListAsync();
            var gizmosByName = await context.Gizmos
                .Where(g => g.Name == "gizmo-in-secondary").ToListAsync();

            Assert.Single(widgetsByName);
            Assert.Single(gizmosByName);
            // A widget value must never surface from the secondary bucket's collection.
            Assert.Empty(await context.Gizmos.Where(g => g.Name == "widget-in-default").ToListAsync());
        }
    }

    public class Widget
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public class Gizmo
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public sealed class SpanningContext(DbContextOptions<SpanningContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets { get; set; } = null!;
        public DbSet<Gizmo> Gizmos { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Both buckets expose an identical isolation.widget keyspace (see AppHost), so the
            // two entity types differ only by bucket.
            modelBuilder.Entity<Widget>().ToCouchbaseCollection("default", "isolation", "widget");
            modelBuilder.Entity<Gizmo>().ToCouchbaseCollection("secondary", "isolation", "widget");
            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }
}
