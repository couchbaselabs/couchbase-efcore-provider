using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Originated as a CBEF-23 step-0 empirical spike -- before implementing any <c>DateTime</c>
/// member/method translator (<c>.Year</c>, <c>.AddDays()</c>, etc.), this proved -- against a real
/// cluster -- what exact string format a <see cref="DateTime"/> ends up in once stored, and
/// whether the existing query-parameter path already compares correctly against it. Neither could
/// be determined by reading source alone: the Couchbase SDK's query-parameter serialization is
/// compiled-only (no source available), and at the time zero other tests in this repo did any
/// DateTime comparison.
/// <para>
/// Kept as a permanent regression test: it guards the foundational assumption every
/// <c>CouchbaseDateTimeMemberTranslator</c>/<c>CouchbaseDateTimeMethodTranslator</c> format string
/// depends on (see their doc comments) -- that the KV write path and the query-parameter path
/// serialize <see cref="DateTime"/> the same way. If a future SDK/serializer change breaks that
/// assumption, every other DateTime-translator test would start failing for a confusing,
/// hard-to-diagnose reason; this test fails first, directly, and says why.
/// </para>
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DateTimeFormatSpikeTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task StoredDateTime_ObservedFormat_AndComparisonBehavior()
    {
        var collectionName = "dtspike" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<SpikeDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.AutoCreateIndexes = true;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        await using var context = new SpikeDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();

            var stamp = new DateTime(2026, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);
            var entity = new SpikeEntity { Id = 1, Stamp = stamp };
            context.Entities.Add(entity);
            await context.SaveChangesAsync();

            // Step 1: observe the exact stored string via a raw N1QL projection, bypassing EF's
            // own type mapping entirely so there's no risk of the read path masking the real
            // on-disk format. Uses the same SystemTextJsonSerializer the provider itself defaults
            // to (CouchbaseOptionsExtension.cs) -- the SDK's own default (Newtonsoft) auto-sniffs
            // ISO-8601-looking strings into DateTimeOffset objects even when a plain string is
            // requested, which would corrupt this observation.
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password)
                .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucketId = fixture.BucketName;
            var scopeId = fixture.ScopeName;
            using var rawResult = await cluster.QueryAsync<string>(
                $"SELECT RAW Stamp FROM `{bucketId}`.`{scopeId}`.`{collectionName}` WHERE Id = 1");

            string? observedFormat = null;
            await foreach (var row in rawResult.Rows)
            {
                observedFormat = row;
            }

            outputHelper.WriteLine($"Observed stored DateTime string: '{observedFormat}'");
            Assert.NotNull(observedFormat);

            // Step 2: does the existing query-parameter path already agree with the stored
            // format? If this fails, that's a separate, pre-existing bug bigger than this ticket
            // -- there's no point building AddDays/.Date on top of a foundation where equality
            // doesn't even work.
            var equalityMatch = await context.Entities.Where(e => e.Stamp == stamp).ToListAsync();
            outputHelper.WriteLine($"Equality match count: {equalityMatch.Count}");
            Assert.Single(equalityMatch);

            // Step 3: does ordering/range comparison work? String comparison only orders
            // correctly if the format is fixed-width/zero-padded.
            var earlier = stamp.AddDays(-1);
            var rangeMatch = await context.Entities.Where(e => e.Stamp > earlier).ToListAsync();
            outputHelper.WriteLine($"Range match count: {rangeMatch.Count}");
            Assert.Single(rangeMatch);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private async Task DropCollectionAsync(string collectionName)
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class SpikeEntity
    {
        public long Id { get; set; }
        public DateTime Stamp { get; set; }
    }

    public class SpikeDbContext(DbContextOptions<SpikeDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<SpikeEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SpikeEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
