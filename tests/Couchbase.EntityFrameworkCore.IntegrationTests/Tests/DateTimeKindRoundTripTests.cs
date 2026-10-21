using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// A <see cref="DateTime"/> is stored as ISO text ending in its kind designator, so a UTC value is
/// written with a "Z" suffix. It must come back as the same UTC value: reading it with a plain
/// <c>DateTime.Parse</c> converted it to the machine's local clock, changing its ticks and Kind.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DateTimeKindRoundTripTests(BloggingFixture fixture)
{
    [Fact]
    public async Task UtcDateTime_RoundTripsAsTheSameUtcValue()
    {
        var collectionName = "dtkind" + Guid.NewGuid().ToString("N");
        var utc = new DateTime(2025, 1, 15, 10, 30, 45, 123, DateTimeKind.Utc);
        var unspecified = new DateTime(2025, 1, 15, 10, 30, 45, 123, DateTimeKind.Unspecified);

        var optionsBuilder = new DbContextOptionsBuilder<DateTimeKindDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
                o.AutoCreateIndexes = true;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        try
        {
            await using (var writer = new DateTimeKindDbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new DateTimeKindEntity { Id = 1, Utc = utc, Unspecified = unspecified });
                await writer.SaveChangesAsync();
            }

            await using var reader = new DateTimeKindDbContext(optionsBuilder.Options, collectionName);
            var loaded = await PollingHelper.PollForResultAsync(
                () => reader.Set<DateTimeKindEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == 1),
                result => result != null,
                TimeSpan.FromSeconds(10));

            Assert.NotNull(loaded);
            Assert.Equal(DateTimeKind.Utc, loaded!.Utc.Kind);
            Assert.Equal(utc, loaded.Utc);
            Assert.Equal(DateTimeKind.Unspecified, loaded.Unspecified.Kind);
            Assert.Equal(unspecified, loaded.Unspecified);
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

    public class DateTimeKindEntity
    {
        public long Id { get; set; }
        public DateTime Utc { get; set; }
        public DateTime Unspecified { get; set; }
    }

    public class DateTimeKindDbContext(DbContextOptions<DateTimeKindDbContext> options, string collectionName)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<DateTimeKindEntity>().ToCouchbaseCollection(this, collectionName);
    }
}
