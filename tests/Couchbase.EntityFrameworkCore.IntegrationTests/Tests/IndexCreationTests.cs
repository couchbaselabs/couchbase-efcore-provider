using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Integration tests for <see cref="Couchbase.EntityFrameworkCore.Infrastructure.ICouchbaseDbContextOptionsBuilder.AutoCreateIndexes"/>:
/// EnsureCreatedAsync optionally creates a primary index on every collection referenced by the
/// model, and waits for it to report online before returning, so a query issued immediately
/// afterward doesn't race the index becoming queryable.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class IndexCreationTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexes_CreatesQueryablePrimaryIndex()
    {
        var collectionName = "idxauto" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<IndexCreationDbContext>();
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

        await using var context = new IndexCreationDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();
            outputHelper.WriteLine($"EnsureCreatedAsync completed for {collectionName}");

            // If the primary index weren't online yet, this query would throw. AutoCreateIndexes
            // is responsible for both creating it and waiting for it to come online before
            // EnsureCreatedAsync returns.
            var results = await context.Entities.ToListAsync();
            Assert.Empty(results);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithoutAutoCreateIndexes_DoesNotCreatePrimaryIndex()
    {
        var collectionName = "idxoff" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<NoIndexDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                // AutoCreateIndexes left at its false default.
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        await using var context = new NoIndexDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();
            outputHelper.WriteLine($"EnsureCreatedAsync completed for {collectionName} (AutoCreateIndexes off)");

            // Assert the direct, deterministic thing this option controls -- no primary index was
            // created for this keyspace -- rather than "does a query against it fail," which turned
            // out to depend on cluster/scope-specific query-service behavior unrelated to this
            // provider (verified empirically: some shared test scopes tolerate an unindexed scan).
            var indexCount = await CountPrimaryIndexesAsync(fixture.BucketName, fixture.ScopeName, collectionName);
            Assert.Equal(0, indexCount);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private async Task<int> CountPrimaryIndexesAsync(string bucketName, string scopeName, string collectionName)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password);
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        using var result = await cluster.QueryAsync<int>(
            "SELECT RAW COUNT(*) FROM system:indexes WHERE is_primary = true "
            + "AND bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection",
            new global::Couchbase.Query.QueryOptions()
                .Parameter("bucket", bucketName)
                .Parameter("scope", scopeName)
                .Parameter("collection", collectionName));

        var count = 0;
        await foreach (var c in result.Rows)
        {
            count = c;
        }

        return count;
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexes_CreatesIndexInEntityMappedBucket()
    {
        // The entity is mapped to the "secondary" bucket (pre-provisioned by the AppHost, see
        // MultiBucketSingleContextTests), while the context itself is configured for a different
        // ("default") bucket. AutoCreateIndexes must create the primary index in "secondary" --
        // the bucket the collection actually lives in -- not just the configured one. This is the
        // exact bug class CreateSequenceAsync has (always targets the configured bucket); this
        // test proves CreateIndexesAsync does not repeat it.
        var collectionName = "idxsecondary" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<SecondaryBucketIndexContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = "default";
                o.Scope = "isolation";
                o.AutoCreateIndexes = true;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        await using var context = new SecondaryBucketIndexContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();

            // Query through EF -- it resolves this entity's actual ("secondary") bucket
            // automatically, same as MultiBucketSingleContextTests already relies on. If the
            // index had only been created in the configured ("default") bucket, this query
            // would fail with a missing-index error.
            var results = await context.Entities.ToListAsync();
            Assert.Empty(results);
        }
        finally
        {
            await DropCollectionAsync(collectionName, bucketName: "secondary", scopeName: "isolation");
        }
    }

    private async Task DropCollectionAsync(string collectionName, string? bucketName = null, string? scopeName = null)
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(bucketName ?? fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(scopeName ?? fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class IndexCreationEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class IndexCreationDbContext(DbContextOptions<IndexCreationDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<IndexCreationEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<IndexCreationEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }

    public class NoIndexEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // A distinct DbContext type from IndexCreationDbContext -- EF Core caches the compiled model
    // per DbContext CLR type by default, calling OnModelCreating only once for the type's whole
    // lifetime in the process. Sharing IndexCreationDbContext between two tests that each pass a
    // different collectionName would mean only the first test's OnModelCreating call (and its
    // collectionName) actually takes effect; the second test would silently operate against the
    // first test's collection instead of its own.
    public class NoIndexDbContext(DbContextOptions<NoIndexDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<NoIndexEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<NoIndexEntity>().ToCouchbaseCollection(this, collectionName);
        }
    }

    public class SecondaryBucketEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SecondaryBucketIndexContext(DbContextOptions<SecondaryBucketIndexContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<SecondaryBucketEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Mapped to the "secondary" bucket while the context is configured for "default".
            modelBuilder.Entity<SecondaryBucketEntity>().ToCouchbaseCollection("secondary", "isolation", collectionName);
        }
    }
}
