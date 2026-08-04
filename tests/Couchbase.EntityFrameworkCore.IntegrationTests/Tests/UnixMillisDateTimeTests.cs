using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves <see cref="UnixMillisDateTimeAttribute"/>/<c>HasUnixMillisDateTime</c> round-trips
/// against a real cluster: stored as a raw JSON <c>NUMBER</c> (not a string), and
/// <c>.Year</c>/<c>.Date</c>/<c>.AddDays</c> query translation actually executes correctly against
/// real data -- not just that the generated SQL text looks right.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class UnixMillisDateTimeTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "unixmillis" + Guid.NewGuid().ToString("N");

    private static readonly DateTime OccurredAt =
        new(DateTime.UtcNow.Year - 1, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);

    private static readonly long OccurredAtMillis = new DateTimeOffset(OccurredAt).ToUnixTimeMilliseconds();

    private EventDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EventDbContext>();
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
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        _context = new EventDbContext(optionsBuilder.Options, CollectionName);
        await _context.Database.EnsureCreatedAsync();

        // Written directly via the KV API as a raw NUMBER, independent of this provider's own
        // write path, to prove the query side reads real millis data correctly rather than only
        // round-tripping data this same provider wrote.
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
            ["OccurredAt"] = OccurredAtMillis,
        });
    }

    [Fact]
    public async Task StoredValue_IsRawNumber_NotString()
    {
        var result = await _context.Database
            .SqlQueryRaw<long>($"SELECT RAW `OccurredAt` FROM `{fixture.BucketName}`.`{fixture.ScopeName}`.`{CollectionName}` WHERE META().id = '1'")
            .ToListAsync();

        Assert.Single(result);
        Assert.Equal(OccurredAtMillis, result[0]);
    }

    [Fact]
    public async Task Year_TranslatesAndExecutesCorrectly()
    {
        var result = await _context.Events.Where(e => e.OccurredAt.Year == OccurredAt.Year).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Date_TranslatesAndExecutesCorrectly()
    {
        var expected = new DateTime(OccurredAt.Year, OccurredAt.Month, OccurredAt.Day, 0, 0, 0, DateTimeKind.Utc);
        var result = await _context.Events.Where(e => e.OccurredAt.Date == expected).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task AddDays_TranslatesAndExecutesCorrectly()
    {
        var dayLater = OccurredAt.AddDays(1);
        var result = await _context.Events.Where(e => e.OccurredAt.AddDays(1) == dayLater).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Equality_AgainstCapturedConstant_TranslatesAndExecutesCorrectly()
    {
        var result = await _context.Events.Where(e => e.OccurredAt == OccurredAt).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task RoundTrip_ThroughSaveChanges_ReadsBackSameValue()
    {
        var newTimestamp = new DateTime(OccurredAt.Year, 7, 4, 12, 0, 0, DateTimeKind.Utc);
        _context.Events.Add(new Event { Id = 2, OccurredAt = newTimestamp });
        await _context.SaveChangesAsync();

        var optionsBuilder = new DbContextOptionsBuilder<EventDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        await using var freshContext = new EventDbContext(optionsBuilder.Options, CollectionName);
        var result = await freshContext.Events.AsNoTracking().FirstAsync(e => e.Id == 2);
        Assert.Equal(newTimestamp, result.OccurredAt);
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

    public class Event
    {
        public long Id { get; set; }

        [UnixMillisDateTime]
        public DateTime OccurredAt { get; set; }
    }

    public class EventDbContext(DbContextOptions<EventDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<Event> Events { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Event>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
