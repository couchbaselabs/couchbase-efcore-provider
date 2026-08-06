using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves <c>EF.Functions.IsMissing</c>/<c>IsNotMissing</c>/<c>IsValued</c>/<c>IsNotValued</c>
/// translate to N1QL's postfix <c>IS [NOT] MISSING</c>/<c>IS [NOT] VALUED</c> operators and
/// correctly distinguish the three ways a field can appear in a Couchbase document: genuinely
/// MISSING (the JSON key doesn't exist at all), present with an explicit JSON <c>null</c>, and
/// present with a real value -- the whole point of this feature, so it needs live proof against a
/// real query engine, not just SQL-text assertions. Mirrors <c>CouchbaseCoalesceTests.cs</c>'s
/// established raw-KV-write pattern for producing a genuinely missing field (SaveChangesAsync
/// always writes every mapped property, so it can never produce one).
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseMissingValuedTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "missingvalued" + Guid.NewGuid().ToString("N");

    private MissingValuedDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<MissingValuedDbContext>();
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

        _context = new MissingValuedDbContext(optionsBuilder.Options, CollectionName);
        await _context.Database.EnsureCreatedAsync();

        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password)
            .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        var scope = await bucket.ScopeAsync(fixture.ScopeName);
        var collection = scope.Collection(CollectionName);

        // Id=1: Title field is genuinely MISSING (key omitted entirely).
        await collection.InsertAsync("1", new Dictionary<string, object> { ["Id"] = 1 });
        // Id=2: Title field is present but an explicit JSON null.
        await collection.InsertAsync("2", new Dictionary<string, object?> { ["Id"] = 2, ["Title"] = null });
        // Id=3: Title field is present with a real value.
        await collection.InsertAsync("3", new Dictionary<string, object> { ["Id"] = 3, ["Title"] = "real value" });
    }

    [Fact]
    public async Task IsMissing_MatchesOnlyTheGenuinelyMissingField()
    {
        var results = await _context.Entities
            .Where(e => EF.Functions.IsMissing(e.Title))
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
    }

    [Fact]
    public async Task IsNotMissing_MatchesExplicitNullAndRealValue_NotGenuinelyMissing()
    {
        var results = await _context.Entities
            .Where(e => EF.Functions.IsNotMissing(e.Title))
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Id == 2);
        Assert.Contains(results, e => e.Id == 3);
    }

    [Fact]
    public async Task IsValued_MatchesOnlyTheRealValue()
    {
        var results = await _context.Entities
            .Where(e => EF.Functions.IsValued(e.Title))
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal(3, results[0].Id);
    }

    [Fact]
    public async Task IsNotValued_MatchesGenuinelyMissingAndExplicitNull_NotRealValue()
    {
        var results = await _context.Entities
            .Where(e => EF.Functions.IsNotValued(e.Title))
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Id == 1);
        Assert.Contains(results, e => e.Id == 2);
    }

    [Fact]
    public async Task IsMissing_Negated_BehavesLikeIsNotMissing()
    {
        var results = await _context.Entities
            .Where(e => !EF.Functions.IsMissing(e.Title))
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Id == 2);
        Assert.Contains(results, e => e.Id == 3);
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

    public class MissingValuedEntity
    {
        public int Id { get; set; }
        public string? Title { get; set; }
    }

    public class MissingValuedDbContext(DbContextOptions<MissingValuedDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<MissingValuedEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<MissingValuedEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
