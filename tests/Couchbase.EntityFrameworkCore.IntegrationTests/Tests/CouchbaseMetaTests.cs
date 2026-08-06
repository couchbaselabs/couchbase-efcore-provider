using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves N1QL <c>META()</c> support against a real cluster: <c>META().cas</c> as an EF Core
/// optimistic-concurrency token (closing the previously-nonexistent concurrent-write detection
/// gap), and read-only <c>META().id</c>/<c>META().expiration</c>/<c>META().flags</c>/<c>META().type</c>
/// access.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseMetaTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "meta" + Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    private MetaDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<MetaDbContext>();
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
        return new MetaDbContext(optionsBuilder.Options, CollectionName);
    }

    [Fact]
    public async Task Cas_RoundTrips_AfterInsertAndUpdate()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        var entity = new MetaEntity { Id = 1, Name = "original" };
        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        Assert.NotEqual(0UL, entity.Cas);
        var casAfterInsert = entity.Cas;

        entity.Name = "updated";
        await ctx.SaveChangesAsync();

        Assert.NotEqual(0UL, entity.Cas);
        Assert.NotEqual(casAfterInsert, entity.Cas);
    }

    [Fact]
    public async Task ConcurrentUpdate_WithStaleCas_ThrowsDbUpdateConcurrencyException()
    {
        await using var setupCtx = CreateContext();
        await setupCtx.Database.EnsureCreatedAsync();
        setupCtx.Entities.Add(new MetaEntity { Id = 2, Name = "original" });
        await setupCtx.SaveChangesAsync();

        // Two independent contexts each read the same document, capturing the same starting CAS.
        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext();
        var entityA = await ctxA.Entities.SingleAsync(e => e.Id == 2);
        var entityB = await ctxB.Entities.SingleAsync(e => e.Id == 2);

        // A writes first and succeeds, advancing the document's real CAS.
        entityA.Name = "changed-by-a";
        await ctxA.SaveChangesAsync();

        // B still holds the pre-A CAS -- its write must be rejected, not silently overwrite A's change.
        entityB.Name = "changed-by-b";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentUpdate_AfterStandardResolutionPattern_RetrySucceeds()
    {
        // EF Core's own documented DbUpdateConcurrencyException resolution pattern only refreshes
        // OriginalValues, never CurrentValues -- if the CAS check read CurrentValue instead of
        // OriginalValue, this exact, textbook-correct pattern would retry forever with the same
        // stale CAS. Proves the fix, not just the initial throw (already covered above).
        await using var setupCtx = CreateContext();
        await setupCtx.Database.EnsureCreatedAsync();
        setupCtx.Entities.Add(new MetaEntity { Id = 7, Name = "original" });
        await setupCtx.SaveChangesAsync();

        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext();
        var entityA = await ctxA.Entities.SingleAsync(e => e.Id == 7);
        var entityB = await ctxB.Entities.SingleAsync(e => e.Id == 7);

        entityA.Name = "changed-by-a";
        await ctxA.SaveChangesAsync();

        entityB.Name = "changed-by-b";
        var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());

        // EF Core's documented resolution: refresh OriginalValues from the database, then retry.
        var entry = ex.Entries.Single();
        var databaseValues = await entry.GetDatabaseValuesAsync();
        entry.OriginalValues.SetValues(databaseValues!);

        await ctxB.SaveChangesAsync();

        await using var verifyCtx = CreateContext();
        var finalEntity = await verifyCtx.Entities.SingleAsync(e => e.Id == 7);
        Assert.Equal("changed-by-b", finalEntity.Name);
    }

    [Fact]
    public async Task DeleteWithStaleCas_ThrowsDbUpdateConcurrencyException()
    {
        await using var setupCtx = CreateContext();
        await setupCtx.Database.EnsureCreatedAsync();
        setupCtx.Entities.Add(new MetaEntity { Id = 3, Name = "original" });
        await setupCtx.SaveChangesAsync();

        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext();
        var entityA = await ctxA.Entities.SingleAsync(e => e.Id == 3);
        var entityB = await ctxB.Entities.SingleAsync(e => e.Id == 3);

        entityA.Name = "changed-by-a";
        await ctxA.SaveChangesAsync();

        ctxB.Entities.Remove(entityB);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    [Fact]
    public async Task DocId_MatchesActualDocumentKey()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Entities.Add(new MetaEntity { Id = 4, Name = "keyed" });
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var entity = await readCtx.Entities.SingleAsync(e => e.Id == 4);

        Assert.Equal("4", entity.DocId);
    }

    [Fact]
    public async Task Expiration_WithNoTtlSet_ReadsAsZero()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Entities.Add(new MetaEntity { Id = 5, Name = "no-ttl" });
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var entity = await readCtx.Entities.SingleAsync(e => e.Id == 5);

        Assert.Equal(0L, entity.Expiration);
    }

    [Fact]
    public async Task Expiration_WithTtlSetViaRawKv_ReadsNonZeroEpochSeconds()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();

        // This provider has no write-side TTL API today -- simulate data written by another
        // process/the SDK directly, exactly the scenario META().expiration exists to read back.
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password)
            .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        var scope = await bucket.ScopeAsync(fixture.ScopeName);
        var collection = scope.Collection(CollectionName);
        var beforeInsert = DateTimeOffset.UtcNow;
        await collection.InsertAsync("6", new Dictionary<string, object>
        {
            ["Id"] = 6,
            ["Name"] = "with-ttl",
        }, new global::Couchbase.KeyValue.InsertOptions().Expiry(TimeSpan.FromMinutes(30)));

        await using var readCtx = CreateContext();
        var entity = await readCtx.Entities.SingleAsync(e => e.Id == 6);

        Assert.True(entity.Expiration > 0, "Expiration should be a nonzero epoch-seconds value when a TTL is set.");

        // The expiration is computed from the *server's* clock at the moment it processes the
        // write, not the client's -- ToUnixTimeSeconds() floors to whole seconds, and even small
        // client/server clock skew (routine under Aspire's containerized cluster) can put the
        // server's floored second one tick below the client's, independent of network latency.
        // A few seconds of slack on the lower bound absorbs that without weakening the assertion's
        // actual purpose (proving a real ~30-minute TTL was read back, not an exact-second match).
        const int clockSkewSlack = 5;
        var expectedNoEarlierThan = beforeInsert.AddMinutes(30).AddSeconds(-clockSkewSlack).ToUnixTimeSeconds();
        var expectedNoLaterThan = DateTimeOffset.UtcNow.AddMinutes(30).AddMinutes(1).ToUnixTimeSeconds();
        Assert.InRange(entity.Expiration, expectedNoEarlierThan, expectedNoLaterThan);
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

    public class MetaEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        [CouchbaseMeta(CouchbaseMetaField.Cas)]
        public ulong Cas { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Id)]
        public string DocId { get; set; } = string.Empty;

        [CouchbaseMeta(CouchbaseMetaField.Expiration)]
        public long Expiration { get; set; }
    }

    public class MetaDbContext(DbContextOptions<MetaDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<MetaEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<MetaEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.Property(e => e.Cas).IsConcurrencyToken();
            });
        }
    }
}

/// <summary>
/// Proves N1QL <c>META().flags</c>/<c>META().type</c> support against a real cluster.
/// </summary>
/// <remarks>
/// <see cref="FlagsTypeEntity"/> deliberately does NOT also carry a <c>[CouchbaseMeta(Expiration)]</c>
/// property -- projecting <c>META(alias).flags</c> together with <c>META(alias).expiration</c> in
/// the same SELECT was found (via an isolated live-cluster spike, bypassing this provider's reader
/// entirely and inspecting the raw N1QL response) to make the Couchbase Server query engine itself
/// return <c>0</c> for <c>flags</c>, regardless of the document's real flags value -- a query-engine
/// bug, not something this provider's SQL generation or materialization causes or can work around.
/// See the "Known limitations" note on <see cref="CouchbaseMetaField.Flags"/> and
/// <c>docs/limitations.md</c>.
/// </remarks>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseMetaFlagsTypeTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "metaflags" + Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    private FlagsTypeDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<FlagsTypeDbContext>();
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
        return new FlagsTypeDbContext(optionsBuilder.Options, CollectionName);
    }

    [Fact]
    public async Task Type_ForJsonDocument_ReadsAsJson()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Entities.Add(new FlagsTypeEntity { Id = 1, Name = "typed" });
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var entity = await readCtx.Entities.SingleAsync(e => e.Id == 1);

        Assert.Equal("json", entity.DocType);
    }

    [Fact]
    public async Task Flags_ForJsonDocument_ReadsAsNonZeroValue()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Entities.Add(new FlagsTypeEntity { Id = 2, Name = "flagged" });
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var entity = await readCtx.Entities.SingleAsync(e => e.Id == 2);

        // The exact bit pattern is an SDK/serializer implementation detail (it encodes datatype
        // hints, not anything this provider controls) -- assert only that a real value comes back,
        // not a specific constant.
        Assert.NotEqual(0u, entity.DocFlags);
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

    public class FlagsTypeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        [CouchbaseMeta(CouchbaseMetaField.Flags)]
        public uint DocFlags { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Type)]
        public string DocType { get; set; } = string.Empty;
    }

    public class FlagsTypeDbContext(DbContextOptions<FlagsTypeDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<FlagsTypeEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FlagsTypeEntity>(b => b.ToCouchbaseCollection(this, collectionName));
        }
    }
}

/// <summary>
/// Documents a confirmed Couchbase Server N1QL query-engine limitation (not a bug in this
/// provider): projecting <c>META(alias).flags</c> together with <c>META(alias).expiration</c> in
/// the same SELECT makes the server return <c>0</c> for <c>flags</c> regardless of the document's
/// real value. Confirmed via a raw <c>QueryAsync&lt;JsonElement&gt;</c> call bypassing this
/// provider's SQL generation and reader entirely -- the wrong value is already present in the raw
/// N1QL response, so there is nothing to fix client-side. If a future Couchbase Server release
/// fixes this, this test starts failing and should be treated as a signal to relax the "avoid
/// combining Flags with Expiration" guidance in <c>docs/limitations.md</c> and on
/// <see cref="CouchbaseMetaField.Flags"/>'s XML doc comment.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseMetaFlagsExpirationKnownLimitationTests(BloggingFixture fixture) : IAsyncLifetime
{
    private static readonly string CollectionName = "metaflagsexp" + Guid.NewGuid().ToString("N");

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Flags_CombinedWithExpirationInSameQuery_ServerReturnsZeroInsteadOfRealValue()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CombinedDbContext>();
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

        await using var ctx = new CombinedDbContext(optionsBuilder.Options, CollectionName);
        await ctx.Database.EnsureCreatedAsync();
        ctx.Entities.Add(new CombinedEntity { Id = 1 });
        await ctx.SaveChangesAsync();

        await using var readCtx = new CombinedDbContext(optionsBuilder.Options, CollectionName);
        var entity = await readCtx.Entities.SingleAsync(e => e.Id == 1);

        // This asserts the CURRENT, CONFIRMED-BUGGY server behavior -- Flags reads back as 0 here
        // even though the document's real flags value is nonzero (proven separately in
        // CouchbaseMetaFlagsTypeTests, which never combines Flags with Expiration in one query).
        Assert.Equal(0u, entity.DocFlags);
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

    public class CombinedEntity
    {
        public int Id { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Flags)]
        public uint DocFlags { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Expiration)]
        public long Expiration { get; set; }
    }

    public class CombinedDbContext(DbContextOptions<CombinedDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<CombinedEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CombinedEntity>(b => b.ToCouchbaseCollection(this, collectionName));
        }
    }
}
