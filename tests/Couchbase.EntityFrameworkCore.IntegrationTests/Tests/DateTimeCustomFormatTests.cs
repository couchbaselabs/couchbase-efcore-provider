using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves the configurable <c>DateTimeFormat</c> option (added after CBEF-23 shipped a hardcoded
/// Go-layout constant) actually round-trips against a real cluster, not just that it generates
/// plausible-looking SQL text (already covered by
/// <c>CouchbaseDateTimeFunctionSqlGenerationTests</c>'s unit tests). The document under test here
/// is written directly via the SDK's KV API with its <c>published</c> field as a plain
/// <c>"yyyy-MM-dd"</c> date-only string -- deliberately NOT this provider's own default
/// serialization -- to simulate the exact scenario that motivated this option: N1QL has no native
/// date type, so data written by another system (or an EF context configured differently) can be
/// in a different, still-legitimate, string convention. The <see cref="DbContext"/> under test is
/// configured with a matching <c>DateTimeFormat</c>, proving the LINQ <c>.Date</c>/<c>.Today</c>
/// translators correctly use the configured format rather than the old hardcoded default.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DateTimeCustomFormatTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "dtfmt" + Guid.NewGuid().ToString("N");
    private static readonly DateTime StoredDate = new(DateTime.UtcNow.Year - 1, 3, 14);

    private CustomFormatDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CustomFormatDbContext>();
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
                // Deliberately different from the default ("yyyy-MM-ddTHH:mm:ss.FFFK") -- a
                // date-only convention with no time component at all.
                o.DateTimeFormat = "yyyy-MM-dd";
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        _context = new CustomFormatDbContext(optionsBuilder.Options, CollectionName);
        await _context.Database.EnsureCreatedAsync();

        // Written directly via the KV API rather than SaveChangesAsync -- this provider's own
        // default DateTime serialization always includes time-of-day/offset, so the only way to
        // get a genuinely date-only stored string (the scenario this test exists to cover) is to
        // bypass it and write the JSON exactly as a different DateTimeFormat convention expects.
        // Must explicitly opt into SystemTextJsonSerializer, matching what CouchbaseOptionsExtension
        // configures for the EF-managed cluster -- the SDK's own raw default (Newtonsoft-based)
        // recases Dictionary keys to camelCase, which would silently corrupt the exact-casing
        // "Id"/"Title"/"Published" field names EF's column-name convention expects.
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password)
            .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        var scope = await bucket.ScopeAsync(fixture.ScopeName);
        var collection = scope.Collection(CollectionName);
        // Regular (non-owned) properties keep their exact C# property name as the JSON field
        // name by default (GetColumnName() == property name unless a [JsonPropertyName]-style
        // attribute or convention overrides it) -- FieldNamingPolicy only affects owned-type
        // navigation field names, not top-level scalar columns. A plain anonymous-object insert
        // would get re-cased by the SDK's default serializer (it applies its own camelCase
        // convention), so use a Dictionary<string, object> instead -- its keys are serialized
        // verbatim, matching how this provider's own write path (HydrateObjectFromEntity)
        // avoids the same pitfall for shared/owned document shapes.
        await collection.InsertAsync("1", new Dictionary<string, object>
        {
            ["Id"] = 1L,
            ["Title"] = "Custom Format Post",
            ["Published"] = StoredDate.ToString("yyyy-MM-dd"),
        });
    }

    [Fact]
    public async Task Date_WithCustomDateOnlyFormat_MatchesStoredDateOnlyString()
    {
        var result = await _context.Entities.Where(e => e.Published.Date == StoredDate).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Date_WithCustomDateOnlyFormat_ComparesBeforeToday()
    {
        // StoredDate is anchored to last year, so it must be strictly before today. Both sides of
        // this comparison (the stored .Date truncation and DateTime.Today's NOW_UTC/DATE_TRUNC_STR
        // translation) must use the SAME configured "yyyy-MM-dd" format for the string comparison
        // to order correctly -- proving DateTimeFormat threads through both call sites.
        var result = await _context.Entities.Where(e => e.Published.Date < DateTime.Today).ToListAsync();
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

    public class CustomFormatEntity
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime Published { get; set; }
    }

    public class CustomFormatDbContext(DbContextOptions<CustomFormatDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<CustomFormatEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CustomFormatEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
