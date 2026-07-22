using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// CBEF-23: proves the new/fixed SQL++ function translations actually execute correctly against a
/// real query engine, not just that they generate plausible-looking SQL text (already covered by
/// the unit tests in <c>Couchbase.EntityFrameworkCore.UnitTests</c>'s <c>*SqlGenerationTests</c>
/// classes). Things like <c>POSITION</c>'s argument order, <c>DATE_ADD_STR</c>'s
/// millis-not-string return type, and <c>LIKE</c> escaping edge cases look right as text but need
/// a live round-trip to be sure.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class FunctionTranslationTests(BloggingFixture fixture, ITestOutputHelper outputHelper) : IAsyncLifetime
{
    private static readonly string CollectionName = "fntrans" + Guid.NewGuid().ToString("N");
    private FunctionTranslationDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<FunctionTranslationDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.AutoCreateIndexes = true;
                // NotBounded (the default) can race a read against the just-completed write --
                // AutoCreateIndexes only guarantees the index is online before EnsureCreatedAsync
                // returns, not that a subsequent write is immediately visible to a query.
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        _context = new FunctionTranslationDbContext(optionsBuilder.Options, CollectionName);
        await _context.Database.EnsureCreatedAsync();

        _context.Entities.Add(new FunctionTranslationEntity
        {
            Id = 1,
            Title = "Hello World",
            Score = -4.7,
            Published = new DateTime(2026, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc),
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task DateTime_Date_TruncatesToStartOfDay()
    {
        // DateTimeKind.Utc matters here -- Published is stored as Utc, and an Unspecified-kind
        // DateTime parameter serializes differently, so a naive `new DateTime(2026, 3, 14)`
        // would never compare equal even though DATE_TRUNC_STR computes the right value.
        var expected = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);
        var result = await _context.Entities.Where(e => e.Published.Date == expected).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task DateTime_Today_ComparesAgainstStoredDate()
    {
        // Published is in the past (2026-03-14); Today at test-run time must be later.
        var result = await _context.Entities.Where(e => e.Published < DateTime.Today).ToListAsync();
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

    [Fact]
    public async Task IndexOf_ReturnsCorrectPosition_NotBoolean()
    {
        var result = await _context.Entities.Where(e => e.Title.IndexOf("World") == 6).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task StartsWith_MatchesPrefix()
    {
        var result = await _context.Entities.Where(e => e.Title.StartsWith("Hello")).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task StartsWith_DoesNotMatchNonPrefix()
    {
        var result = await _context.Entities.Where(e => e.Title.StartsWith("World")).ToListAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task EndsWith_MatchesSuffix()
    {
        var result = await _context.Entities.Where(e => e.Title.EndsWith("World")).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task StartsWith_WithLiteralWildcard_TreatsPercentLiterally()
    {
        // Confirms the escaping added for StartsWith/EndsWith actually prevents '%' from acting
        // as a wildcard against a real query engine, not just in generated SQL text.
        var result = await _context.Entities.Where(e => e.Title.StartsWith("Hello%")).ToListAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task Length_ReturnsCorrectCount()
    {
        var result = await _context.Entities.Where(e => e.Title.Length == 11).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task PadLeft_ProducesExpectedString()
    {
        // "Hello World" is 11 characters; PadLeft(15) prepends 4 spaces.
        var result = await _context.Entities
            .Where(e => e.Title.PadLeft(15) == "    Hello World")
            .ToListAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task Abs_ReturnsPositiveValue()
    {
        var result = await _context.Entities.Where(e => Math.Abs(e.Score) == 4.7).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Ceiling_RoundsUp()
    {
        var result = await _context.Entities.Where(e => Math.Ceiling(e.Score) == -4).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Floor_RoundsDown()
    {
        var result = await _context.Entities.Where(e => Math.Floor(e.Score) == -5).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Round_WithDigits_RoundsCorrectly()
    {
        var result = await _context.Entities.Where(e => Math.Round(e.Score, 0) == -5).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task Sign_ReturnsNegativeOne()
    {
        var result = await _context.Entities.Where(e => Math.Sign(e.Score) == -1).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task NewGuid_ProjectsNonEmptyValue()
    {
        // A projection that also references a real column keeps a normal FROM clause -- the
        // realistic usage shape (e.g. `.Select(e => new { e.Title, Guid.NewGuid() })`). A
        // projection where the lambda parameter is entirely unused (`.Select(e => Guid.NewGuid())`)
        // hits a separate, narrower, pre-existing gap: EF Core hoists such expressions into a
        // FROM-less `SELECT UUID()`, which this provider's reader doesn't materialize correctly --
        // out of scope here (a query-shape edge case, not a function-translation bug) but worth
        // flagging to the user.
        var query = _context.Entities.Select(e => new { e.Title, Guid = Guid.NewGuid() });
        outputHelper.WriteLine($"SQL: {query.ToQueryString()}");

        var results = await query.ToListAsync();
        Assert.Single(results);
        Assert.NotEqual(Guid.Empty, results[0].Guid);
    }

    [Fact]
    public async Task DateTime_Year_MatchesStoredYear()
    {
        var result = await _context.Entities.Where(e => e.Published.Year == 2026).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task DateTime_Month_MatchesStoredMonth()
    {
        var result = await _context.Entities.Where(e => e.Published.Month == 3).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task DateTime_AddDays_ProducesUsableDateTimeForComparison()
    {
        // DATE_ADD_STR returns the resulting date as a string directly (see
        // CouchbaseDateTimeMethodTranslator's doc comment); this proves the comparison actually
        // executes and matches against real stored data, not just that it generates plausible SQL.
        var expected = new DateTime(2026, 3, 15, 9, 26, 53, 123, DateTimeKind.Utc);
        var result = await _context.Entities.Where(e => e.Published.AddDays(1) == expected).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task DateTime_AddYears_ProducesUsableDateTimeForComparison()
    {
        var expected = new DateTime(2027, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);
        var result = await _context.Entities.Where(e => e.Published.AddYears(1) == expected).ToListAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task DateTime_UtcNow_ComparesAgainstStoredDate()
    {
        // Published is in the past (2026-03-14); UtcNow at test-run time must be later.
        var result = await _context.Entities.Where(e => e.Published < DateTime.UtcNow).ToListAsync();
        Assert.Single(result);
    }

    public class FunctionTranslationEntity
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public double Score { get; set; }
        public DateTime Published { get; set; }
    }

    public class FunctionTranslationDbContext(DbContextOptions<FunctionTranslationDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<FunctionTranslationEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FunctionTranslationEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
