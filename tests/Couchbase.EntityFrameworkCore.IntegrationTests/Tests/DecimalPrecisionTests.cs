using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Decimals used to be read back through a <c>double</c>, which kept only ~15 significant digits.
/// The reader now parses the JSON number text directly, so everything the query engine sends back
/// survives. The engine itself returns numbers as IEEE doubles, so values with more than 17
/// significant digits are still rounded server-side (the stored document keeps every digit).
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DecimalPrecisionTests(BloggingFixture fixture) : IAsyncLifetime
{
    // Static because EF caches the model (and so the collection name) per context type.
    private static readonly string CollectionName = "decimalprec" + Guid.NewGuid().ToString("N");

    private DbContextOptions<DecimalDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DecimalDbContext>();
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
        _options = optionsBuilder.Options;

        await using var context = new DecimalDbContext(_options, CollectionName);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
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
    public async Task Decimals_RoundTripThroughQueries_UpToDoublePrecision()
    {
        await using (var writer = new DecimalDbContext(_options, CollectionName))
        {
            writer.AddRange(
                new DecimalEntity { Id = 1, Amount = 1234.567890123456m },
                new DecimalEntity { Id = 2, Amount = 1.00000000000001m },
                new DecimalEntity { Id = 3, Amount = 79228162514264.33m },
                new DecimalEntity { Id = 4, Amount = 0.1234567890123456m },
                new DecimalEntity { Id = 5, Amount = 12.5m, Optional = 98765.4321098765m });
            await writer.SaveChangesAsync();
        }

        await using var reader = new DecimalDbContext(_options, CollectionName);
        var all = await PollingHelper.PollForResultAsync(
            () => reader.Set<DecimalEntity>().AsNoTracking().OrderBy(e => e.Id).ToListAsync(),
            result => result.Count >= 5,
            TimeSpan.FromSeconds(10));

        Assert.Equal(1234.567890123456m, all[0].Amount);
        Assert.Equal(1.00000000000001m, all[1].Amount);
        Assert.Equal(79228162514264.33m, all[2].Amount);
        Assert.Equal(0.1234567890123456m, all[3].Amount);
        Assert.Equal(12.5m, all[4].Amount);
        Assert.Equal(98765.4321098765m, all[4].Optional);
        Assert.All(all.Take(4), e => Assert.Null(e.Optional));
    }

    [Fact]
    public async Task DecimalBeyondDoublePrecision_IsStoredExactly_ButTheQueryEngineRoundsIt()
    {
        const decimal value = 1.0000000000000000000000000001m;

        await using (var writer = new DecimalDbContext(_options, CollectionName))
        {
            writer.Add(new DecimalEntity { Id = 1, Amount = value });
            await writer.SaveChangesAsync();
        }

        await using var reader = new DecimalDbContext(_options, CollectionName);

        // SQL++ numbers are IEEE doubles, so a projected value is rounded by the server and the
        // reader cannot recover the lost digits. Pin the observable behaviour to within double precision.
        var materialized = await PollingHelper.PollForResultAsync(
            () => reader.Set<DecimalEntity>().AsNoTracking().ToListAsync(),
            result => result.Count >= 1,
            TimeSpan.FromSeconds(10));
        Assert.InRange(Math.Abs(materialized.Single().Amount - value), 0m, 0.000000000000001m);

        // The stored document still keeps every digit. Selecting the whole document hands back its
        // original bytes (projecting the field is what rounds it).
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password)
            .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var result = await cluster.QueryAsync<System.Text.Json.JsonElement>(
            $"SELECT RAW d FROM `{fixture.BucketName}`.`{fixture.ScopeName}`.`{CollectionName}` d",
            new global::Couchbase.Query.QueryOptions().ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus));
        var storedDocuments = new List<string>();
        await foreach (var row in result) storedDocuments.Add(row.GetRawText());
        Assert.Contains("1.0000000000000000000000000001", Assert.Single(storedDocuments));
    }

    public class DecimalEntity
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public decimal? Optional { get; set; }
    }

    public class DecimalDbContext(DbContextOptions<DecimalDbContext> options, string collectionName) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<DecimalEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
                b.Property(e => e.Amount).HasPrecision(38, 28);
                b.Property(e => e.Optional).HasPrecision(38, 28);
            });
    }
}
