using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Multi-document Couchbase transaction coverage spanning two buckets on the same cluster.
/// <see cref="MultiBucketSingleContextTests"/> already proves a single DbContext can write/read/
/// query across buckets; this class proves <c>BeginCouchbaseTransactionAsync</c> gives those
/// cross-bucket writes real all-or-nothing semantics — a commit persists both, a rollback (or a
/// failure partway through) persists neither.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class CrossBucketTransactionTests(BloggingFixture fixture)
{
    private async Task<ServiceProvider> BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddCouchbase<SpanningContext>(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = "default";
                o.Scope = "isolation";
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            },
            o => o.UseCamelCaseNamingConvention());

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    [Fact]
    public async Task BeginTransaction_Commit_PersistsBothBuckets()
    {
        await using var provider = await BuildProviderAsync();
        const int id = 6001;

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Add(new TxWidgetA { Id = id, Name = "widget-a-committed" });
            context.Add(new TxWidgetB { Id = id, Name = "widget-b-committed" });
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();
            TxWidgetA? widgetA = null;
            TxWidgetB? widgetB = null;
            try
            {
                widgetA = await context.WidgetsA.FindAsync(id);
                widgetB = await context.WidgetsB.FindAsync(id);

                Assert.NotNull(widgetA);
                Assert.NotNull(widgetB);
                Assert.Equal("widget-a-committed", widgetA!.Name);
                Assert.Equal("widget-b-committed", widgetB!.Name);
            }
            finally
            {
                // Clean up even if an assertion (or FindAsync) fails above — otherwise these
                // documents would leak into subsequent test runs.
                if (widgetA != null) context.Remove(widgetA);
                if (widgetB != null) context.Remove(widgetB);
                await context.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task BeginTransaction_Rollback_PersistsNeitherBucket()
    {
        await using var provider = await BuildProviderAsync();
        const int id = 6002;

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Add(new TxWidgetA { Id = id, Name = "widget-a-rolled-back" });
            context.Add(new TxWidgetB { Id = id, Name = "widget-b-rolled-back" });
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        // Verify from a fresh, untracked context — FindAsync on the same context that added
        // these entities would return them straight from the change tracker (still State=Added)
        // without ever touching the server, masking whether the rollback actually took effect.
        // Rollback here never touched the server either (CouchbaseDbTransaction.Rollback just
        // discards the buffered ops), so no polling for eventual consistency is needed.
        await using var verifyScope = provider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<SpanningContext>();
        Assert.Null(await verifyContext.WidgetsA.FindAsync(id));
        Assert.Null(await verifyContext.WidgetsB.FindAsync(id));
    }

    [Fact]
    public async Task BeginTransaction_FailureInOneBucket_RollsBackWriteToOtherBucket()
    {
        await using var provider = await BuildProviderAsync();
        const int newId = 6003;
        const int conflictingId = 6004;

        // Seed a document directly (outside the transaction) so the in-transaction insert to the
        // same key in bucket "secondary" below is guaranteed to conflict. Use Update (upsert)
        // rather than Add (insert) so seeding is idempotent — a leftover document from a prior
        // failed run must not make this step itself throw before the finally block can clean up.
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<SpanningContext>();
            seedContext.Update(new TxWidgetB { Id = conflictingId, Name = "pre-existing" });
            await seedContext.SaveChangesAsync();
        }

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            // A genuinely new document in bucket "default"...
            context.Add(new TxWidgetA { Id = newId, Name = "should-not-persist" });
            // ...and an Insert of an already-existing key in bucket "secondary", which the
            // transaction will fail to commit.
            context.Add(new TxWidgetB { Id = conflictingId, Name = "conflicting-insert" });
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<global::Couchbase.Client.Transactions.Error.TransactionFailedException>(
                () => transaction.CommitAsync());

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<SpanningContext>();

            // The default-bucket write must not have survived the failed transaction — proving
            // the two buckets were rolled back together, not independently.
            Assert.Null(await verifyContext.WidgetsA.FindAsync(newId));
            // The secondary-bucket document must still hold its pre-existing value, not the
            // conflicting insert's value.
            var widgetB = await verifyContext.WidgetsB.FindAsync(conflictingId);
            Assert.NotNull(widgetB);
            Assert.Equal("pre-existing", widgetB!.Name);
        }
        finally
        {
            await using var cleanupScope = provider.CreateAsyncScope();
            var cleanupContext = cleanupScope.ServiceProvider.GetRequiredService<SpanningContext>();
            var leftoverA = await cleanupContext.WidgetsA.FindAsync(newId);
            if (leftoverA != null) cleanupContext.Remove(leftoverA);
            var widgetB = await cleanupContext.WidgetsB.FindAsync(conflictingId);
            if (widgetB != null) cleanupContext.Remove(widgetB);
            await cleanupContext.SaveChangesAsync();
        }
    }

    public class TxWidgetA
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public class TxWidgetB
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public sealed class SpanningContext(DbContextOptions<SpanningContext> options) : DbContext(options)
    {
        public DbSet<TxWidgetA> WidgetsA { get; set; } = null!;
        public DbSet<TxWidgetB> WidgetsB { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Reuses the isolation.widget keyspace the AppHost already provisions in both buckets
            // (see MultiBucketSingleContextTests) — distinct CLR types keep this class's ids from
            // colliding with that test's.
            modelBuilder.Entity<TxWidgetA>().ToCouchbaseCollection("default", "isolation", "widget");
            modelBuilder.Entity<TxWidgetB>().ToCouchbaseCollection("secondary", "isolation", "widget");
            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }
}
