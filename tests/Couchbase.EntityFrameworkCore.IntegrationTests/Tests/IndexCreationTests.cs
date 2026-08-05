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

    private async Task<int> CountOnlineSecondaryIndexesAsync(
        string bucketName, string scopeName, string collectionName, string indexName)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password);
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        // No is_primary filter: a secondary index's system:indexes row was empirically observed
        // to omit that field entirely rather than set it to false, so "is_primary = false" never
        // matches (see CouchbaseDatabaseCreator.WaitForSecondaryIndexOnlineAsync's own comment).
        using var result = await cluster.QueryAsync<int>(
            "SELECT RAW COUNT(*) FROM system:indexes WHERE state = 'online' "
            + "AND bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection AND name = $name",
            new global::Couchbase.Query.QueryOptions()
                .Parameter("bucket", bucketName)
                .Parameter("scope", scopeName)
                .Parameter("collection", collectionName)
                .Parameter("name", indexName));

        var count = 0;
        await foreach (var c in result.Rows)
        {
            count = c;
        }

        return count;
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexes_CreatesQueryableSingleFieldSecondaryIndex()
    {
        var collectionName = "idxsecfield" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<SingleFieldIndexDbContext>();
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

        await using var context = new SingleFieldIndexDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();
            outputHelper.WriteLine($"EnsureCreatedAsync completed for {collectionName}");

            var onlineCount = await CountOnlineSecondaryIndexesAsync(
                fixture.BucketName, fixture.ScopeName, collectionName, "ix_singlefield_score");
            Assert.Equal(1, onlineCount);

            // Re-running EnsureCreatedAsync must not error (CREATE INDEX ... IF NOT EXISTS is
            // idempotent server-side, and the collection/primary-index/sequence steps ahead of it
            // are all already-proven idempotent).
            await context.Database.EnsureCreatedAsync();
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexes_CreatesQueryableCompositeSecondaryIndex()
    {
        var collectionName = "idxseccomp" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<CompositeIndexDbContext>();
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

        await using var context = new CompositeIndexDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();
            outputHelper.WriteLine($"EnsureCreatedAsync completed for {collectionName}");

            var onlineCount = await CountOnlineSecondaryIndexesAsync(
                fixture.BucketName, fixture.ScopeName, collectionName, "ix_composite_score_category");
            Assert.Equal(1, onlineCount);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexes_CreatesQueryableFilteredSecondaryIndex()
    {
        var collectionName = "idxsecfilt" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<FilteredIndexDbContext>();
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

        await using var context = new FilteredIndexDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();
            outputHelper.WriteLine($"EnsureCreatedAsync completed for {collectionName}");

            var onlineCount = await CountOnlineSecondaryIndexesAsync(
                fixture.BucketName, fixture.ScopeName, collectionName, "ix_filtered_score");
            Assert.Equal(1, onlineCount);

            // Prove the filter clause was actually applied, not just that an index by this name
            // exists: a query matching the filter's condition should be plannable/executable
            // (the point of the filter is to keep the index small, not to reject non-matching
            // queries -- this just confirms the WHERE clause was valid N1QL the query service
            // accepted at CREATE INDEX time).
            var results = await context.Entities.Where(e => e.Score > 0).ToListAsync();
            Assert.Empty(results);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexes_CreatesSecondaryIndexInEntityMappedBucket()
    {
        // Mirrors EnsureCreatedAsync_WithAutoCreateIndexes_CreatesIndexInEntityMappedBucket for
        // primary indexes: the entity is mapped to the "secondary" bucket while the context itself
        // is configured for "default". The secondary (HasIndex) index must be created in
        // "secondary" -- the bucket the collection actually lives in -- not the configured one.
        var collectionName = "idxsecbucket" + Guid.NewGuid().ToString("N");

        var optionsBuilder = new DbContextOptionsBuilder<SecondaryBucketSecondaryIndexContext>();
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

        await using var context = new SecondaryBucketSecondaryIndexContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();

            var onlineCount = await CountOnlineSecondaryIndexesAsync(
                "secondary", "isolation", collectionName, "ix_secondarybucket_score");
            Assert.Equal(1, onlineCount);
        }
        finally
        {
            await DropCollectionAsync(collectionName, bucketName: "secondary", scopeName: "isolation");
        }
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

    public class SingleFieldIndexEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    public class SingleFieldIndexDbContext(DbContextOptions<SingleFieldIndexDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<SingleFieldIndexEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SingleFieldIndexEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasIndex(e => e.Score).HasDatabaseName("ix_singlefield_score");
            });
        }
    }

    public class CompositeIndexEntity
    {
        public long Id { get; set; }
        public double Score { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    public class CompositeIndexDbContext(DbContextOptions<CompositeIndexDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<CompositeIndexEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CompositeIndexEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasIndex(e => new { e.Score, e.Category }).HasDatabaseName("ix_composite_score_category");
            });
        }
    }

    public class FilteredIndexEntity
    {
        public long Id { get; set; }
        public double Score { get; set; }
    }

    public class FilteredIndexDbContext(DbContextOptions<FilteredIndexDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<FilteredIndexEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FilteredIndexEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasIndex(e => e.Score).HasDatabaseName("ix_filtered_score").HasFilter("`Score` > 0");
            });
        }
    }

    public class SecondaryBucketSecondaryIndexEntity
    {
        public long Id { get; set; }
        public double Score { get; set; }
    }

    public class SecondaryBucketSecondaryIndexContext(
        DbContextOptions<SecondaryBucketSecondaryIndexContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<SecondaryBucketSecondaryIndexEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Mapped to the "secondary" bucket while the context is configured for "default".
            modelBuilder.Entity<SecondaryBucketSecondaryIndexEntity>(b =>
            {
                b.ToCouchbaseCollection("secondary", "isolation", collectionName);
                b.HasIndex(e => e.Score).HasDatabaseName("ix_secondarybucket_score");
            });
        }
    }
}
