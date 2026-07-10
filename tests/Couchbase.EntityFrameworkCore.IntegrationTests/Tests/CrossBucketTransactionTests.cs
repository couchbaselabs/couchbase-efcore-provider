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

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SpanningContext>();
            await context.Database.EnsureCreatedAsync();
        }
        catch
        {
            // Dispose the provider (and its cluster connection) before propagating — otherwise a
            // failure here would leak it, since the caller never receives a provider to dispose.
            await provider.DisposeAsync();
            throw;
        }

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
                // KV reads aren't governed by QueryScanConsistency.RequestPlus (that only affects
                // N1QL), so a get immediately after CommitAsync can momentarily still return null.
                // Poll with a bounded timeout, matching TransactionTests' pattern.
                widgetA = await PollingHelper.PollForResultAsync(
                    () => context.WidgetsA.FindAsync(id).AsTask(),
                    result => result != null,
                    TimeSpan.FromSeconds(5));
                widgetB = await PollingHelper.PollForResultAsync(
                    () => context.WidgetsB.FindAsync(id).AsTask(),
                    result => result != null,
                    TimeSpan.FromSeconds(5));

                Assert.NotNull(widgetA);
                Assert.NotNull(widgetB);
                Assert.Equal("widget-a-committed", widgetA!.Name);
                Assert.Equal("widget-b-committed", widgetB!.Name);
            }
            finally
            {
                // Re-check with the same bounded polling rather than trusting widgetA/widgetB
                // from the try block: if the poll above timed out (or an assertion threw before
                // they were assigned), the documents can still exist on the server and must not
                // be skipped here, or they'd leak into subsequent test runs.
                var cleanupA = await PollingHelper.PollForResultAsync(
                    () => context.WidgetsA.FindAsync(id).AsTask(),
                    result => result != null,
                    TimeSpan.FromSeconds(5));
                var cleanupB = await PollingHelper.PollForResultAsync(
                    () => context.WidgetsB.FindAsync(id).AsTask(),
                    result => result != null,
                    TimeSpan.FromSeconds(5));
                if (cleanupA != null) context.Remove(cleanupA);
                if (cleanupB != null) context.Remove(cleanupB);
                await context.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task BeginTransaction_Rollback_PersistsNeitherBucket()
    {
        await using var provider = await BuildProviderAsync();
        const int id = 6002;

        try
        {
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
            // A single immediate read could still (rarely) miss a persisted document if a KV read
            // is momentarily stale right after transaction completion, letting an atomic-rollback
            // regression pass unnoticed — poll briefly for the document to appear, then assert it
            // never did, rather than checking only once.
            await using var verifyScope = provider.CreateAsyncScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<SpanningContext>();
            var leakedWidgetA = await PollingHelper.PollForResultAsync(
                () => verifyContext.WidgetsA.FindAsync(id).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            Assert.Null(leakedWidgetA);
            var leakedWidgetB = await PollingHelper.PollForResultAsync(
                () => verifyContext.WidgetsB.FindAsync(id).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            Assert.Null(leakedWidgetB);
        }
        finally
        {
            // If rollback regressed (exactly what this test guards against) or an assertion
            // above failed, the documents could actually be persisted — clean them up so they
            // don't leak into later test runs and make other integration tests flaky. Poll
            // (briefly) rather than a single immediate read per the same reasoning as the other
            // cleanup blocks in this file.
            await using var cleanupScope = provider.CreateAsyncScope();
            var cleanupContext = cleanupScope.ServiceProvider.GetRequiredService<SpanningContext>();
            var leftoverA = await PollingHelper.PollForResultAsync(
                () => cleanupContext.WidgetsA.FindAsync(id).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            if (leftoverA != null) cleanupContext.Remove(leftoverA);
            var leftoverB = await PollingHelper.PollForResultAsync(
                () => cleanupContext.WidgetsB.FindAsync(id).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            if (leftoverB != null) cleanupContext.Remove(leftoverB);
            await cleanupContext.SaveChangesAsync();
        }
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
            // the two buckets were rolled back together, not independently. A single immediate
            // read risks a false pass if the document is merely momentarily stale right after
            // the failed commit and becomes visible moments later, so poll for a short window
            // and fail as soon as it ever appears rather than checking only once.
            var leakedWidgetA = await PollingHelper.PollForResultAsync(
                () => verifyContext.WidgetsA.FindAsync(newId).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            Assert.Null(leakedWidgetA);
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

            // Poll (briefly) rather than a single immediate read: if the transaction
            // unexpectedly persisted data, or the test failed partway through, a momentarily
            // stale KV read here could miss a leftover document and leak it into later runs.
            // Short timeout because the common case is a genuinely absent document (the
            // transaction correctly rolled back), where this always runs to the full timeout.
            var leftoverA = await PollingHelper.PollForResultAsync(
                () => cleanupContext.WidgetsA.FindAsync(newId).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            if (leftoverA != null) cleanupContext.Remove(leftoverA);
            var widgetB = await PollingHelper.PollForResultAsync(
                () => cleanupContext.WidgetsB.FindAsync(conflictingId).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
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
            // (see MultiBucketSingleContextTests). Document keys are derived from the primary key
            // value alone (no type prefix), so this class's ids (6001+) are deliberately outside
            // that test's id range (1) to avoid colliding in the shared collection.
            modelBuilder.Entity<TxWidgetA>().ToCouchbaseCollection("default", "isolation", "widget");
            modelBuilder.Entity<TxWidgetB>().ToCouchbaseCollection("secondary", "isolation", "widget");
            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }
}
