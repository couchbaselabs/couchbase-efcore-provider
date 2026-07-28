using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves the per-property <see cref="DateTimeFormatAttribute"/> override round-trips against a
/// real cluster, independently of the DbContext-wide <c>DateTimeFormat</c> default -- both formats
/// are exercised on two different properties of the SAME document, proving neither cross-
/// contaminates the other.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DateTimePerPropertyFormatTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "dtppfmt" + Guid.NewGuid().ToString("N");

    // Published uses the context-wide default format; ShipDate is anchored to a date-only value
    // to make the per-property "yyyy-MM-dd" override meaningful (no time-of-day to lose).
    private static readonly DateTime Published = new(DateTime.UtcNow.Year - 1, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);
    private static readonly DateTime ShipDate = new(DateTime.UtcNow.Year - 1, 6, 1);

    private MixedFormatDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<MixedFormatDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.AutoCreateIndexes = true;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
                // DateTimeFormat deliberately left at its default -- Published uses it unchanged;
                // only ShipDate gets a per-property override via [DateTimeFormat] on the entity.
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        _context = new MixedFormatDbContext(optionsBuilder.Options, CollectionName);
        await _context.Database.EnsureCreatedAsync();

        // Written directly via the KV API, each field in its own convention, to prove the two
        // formats are read back correctly independently rather than relying on this provider's
        // own (format-consistent) write path to mask a translator that only worked by accident.
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password)
            .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        var scope = await bucket.ScopeAsync(fixture.ScopeName);
        var collection = scope.Collection(CollectionName);
        await collection.InsertAsync("1", new Dictionary<string, object>
        {
            ["Id"] = 1L,
            ["Published"] = Published.ToString("yyyy-MM-ddTHH:mm:ss.FFFK"),
            ["ShipDate"] = ShipDate.ToString("yyyy-MM-dd"),
        });
    }

    [Fact]
    public async Task Date_OnDefaultFormatProperty_MatchesFullPrecisionStoredString()
    {
        var expected = new DateTime(Published.Year, Published.Month, Published.Day, 0, 0, 0, DateTimeKind.Utc);
        var result = await _context.Entities.Where(e => e.Published.Date == expected).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Date_OnPerPropertyOverrideProperty_MatchesDateOnlyStoredString()
    {
        var result = await _context.Entities.Where(e => e.ShipDate.Date == ShipDate).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Date_OnBothProperties_InOneQuery_UseTheirOwnFormatsIndependently()
    {
        // Proves the two formats don't cross-contaminate when both are evaluated together --
        // if either translator picked up the wrong format, one side (or both) would fail to match.
        var expectedPublished = new DateTime(Published.Year, Published.Month, Published.Day, 0, 0, 0, DateTimeKind.Utc);
        var result = await _context.Entities
            .Where(e => e.Published.Date == expectedPublished && e.ShipDate.Date == ShipDate)
            .ToListAsync();

        Assert.Single(result);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();

        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, CollectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class MixedFormatEntity
    {
        public long Id { get; set; }
        public DateTime Published { get; set; }

        [DateTimeFormat("yyyy-MM-dd")]
        public DateTime ShipDate { get; set; }
    }

    public class MixedFormatDbContext(DbContextOptions<MixedFormatDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<MixedFormatEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<MixedFormatEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
