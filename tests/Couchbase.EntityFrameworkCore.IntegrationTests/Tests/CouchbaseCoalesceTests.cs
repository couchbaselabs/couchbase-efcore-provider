using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves C#'s <c>??</c> translates to N1QL's <c>IFMISSINGORNULL</c> and correctly handles both
/// ways a field can be "absent" in a Couchbase document: genuinely MISSING (the JSON key doesn't
/// exist at all) and present with an explicit JSON <c>null</c> -- the whole reason
/// <c>IFMISSINGORNULL</c> was chosen over a generic <c>COALESCE</c>/<c>IFNULL</c>, so it needs
/// live proof against a real query engine, not just SQL-text assertions.
/// </summary>
/// <remarks>
/// Uses a <c>.Where(...)</c> predicate and an anonymous-type <c>.Select(...)</c> to exercise the
/// coalesce, not a *bare* scalar <c>.Select(e => expr)</c> with no wrapper -- that shape hits an
/// unrelated, pre-existing gap in this provider's projection aliasing for un-named scalar
/// expressions (confirmed empirically: it also fails for a plain, non-coalesce property
/// projection, and is the same class of gap already flagged in
/// <c>FunctionTranslationTests.NewGuid_ProjectsNonEmptyValue</c>'s own comment about a
/// FROM-less/unusual projection shape this provider's reader doesn't materialize correctly).
/// Not attempted here -- out of scope for the coalesce fix.
/// </remarks>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseCoalesceTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "coalesce" + Guid.NewGuid().ToString("N");

    private CoalesceDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CoalesceDbContext>();
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

        _context = new CoalesceDbContext(optionsBuilder.Options, CollectionName);
        await _context.Database.EnsureCreatedAsync();

        // Written directly via the KV API -- SaveChangesAsync always writes every property (as a
        // real field, even when its value is null), so it can never produce a genuinely MISSING
        // field. That's exactly the scenario this test exists to cover: data written by another
        // process/an older schema version can omit a field entirely, which JSON null does not
        // capture.
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
    public async Task Coalesce_OnMissingField_ReturnsDefault()
    {
        var result = await _context.Entities
            .Where(e => e.Id == 1)
            .Select(e => new { Title = e.Title ?? "default" })
            .SingleAsync();

        Assert.Equal("default", result.Title);
    }

    [Fact]
    public async Task Coalesce_OnExplicitJsonNull_ReturnsDefault()
    {
        var result = await _context.Entities
            .Where(e => e.Id == 2)
            .Select(e => new { Title = e.Title ?? "default" })
            .SingleAsync();

        Assert.Equal("default", result.Title);
    }

    [Fact]
    public async Task Coalesce_OnRealValue_ReturnsRealValue()
    {
        var result = await _context.Entities
            .Where(e => e.Id == 3)
            .Select(e => new { Title = e.Title ?? "default" })
            .SingleAsync();

        Assert.Equal("real value", result.Title);
    }

    [Fact]
    public async Task Coalesce_InWherePredicate_MatchesOnMissingAndExplicitNull()
    {
        var results = await _context.Entities
            .Where(e => (e.Title ?? "default") == "default")
            .ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Id == 1);
        Assert.Contains(results, e => e.Id == 2);
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

    public class CoalesceEntity
    {
        public int Id { get; set; }
        public string? Title { get; set; }
    }

    public class CoalesceDbContext(DbContextOptions<CoalesceDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<CoalesceEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CoalesceEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }
}
