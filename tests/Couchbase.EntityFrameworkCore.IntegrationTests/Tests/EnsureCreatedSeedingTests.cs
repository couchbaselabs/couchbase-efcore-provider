using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// EF Core's own <c>EnsureCreatedAsync</c> runs the seeding configured with
/// <c>UseSeeding</c>/<c>UseAsyncSeeding</c>; the Couchbase creator must too, otherwise seed data is
/// silently never loaded.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class EnsureCreatedSeedingTests(BloggingFixture fixture)
{
    [Fact]
    public async Task EnsureCreatedAsync_RunsAsyncSeeding_AndReportsWhetherSchemaWasCreated()
    {
        var collectionName = "seed" + Guid.NewGuid().ToString("N");
        var createdFlags = new List<bool>();

        var optionsBuilder = CreateOptions<SeededDbContext>();
        optionsBuilder.UseAsyncSeeding(async (context, created, cancellationToken) =>
        {
            createdFlags.Add(created);

            // Idempotent, as EF Core's seeding guidance recommends: EnsureCreatedAsync runs the
            // delegate on every call, not just when the schema was just created.
            if (await context.Set<SeedEntity>().FindAsync([1L], cancellationToken) == null)
            {
                context.Add(new SeedEntity { Id = 1, Name = "seeded" });
                await context.SaveChangesAsync(cancellationToken);
            }
        });

        await using var context = new SeededDbContext(optionsBuilder.Options, collectionName);

        try
        {
            await context.Database.EnsureCreatedAsync();

            var seeded = await PollingHelper.PollForResultAsync(
                () => context.Set<SeedEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == 1),
                result => result != null,
                TimeSpan.FromSeconds(5));
            Assert.NotNull(seeded);
            Assert.Equal("seeded", seeded!.Name);

            // A second call finds the schema already there, so the flag is false and the idempotent
            // delegate leaves the data alone.
            await context.Database.EnsureCreatedAsync();

            Assert.Equal([true, false], createdFlags);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithOnlySyncSeeding_ThrowsExplainingAsyncSeedingIsNeeded()
    {
        var collectionName = "seedsync" + Guid.NewGuid().ToString("N");

        var optionsBuilder = CreateOptions<SyncOnlySeededDbContext>();
        optionsBuilder.UseSeeding((_, _) => { });

        await using var context = new SyncOnlySeededDbContext(optionsBuilder.Options, collectionName);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Database.EnsureCreatedAsync());
            Assert.Contains("UseAsyncSeeding", exception.Message);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private DbContextOptionsBuilder<TContext> CreateOptions<TContext>() where TContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return optionsBuilder;
    }

    private async Task DropCollectionAsync(string collectionName)
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class SeedEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // Distinct DbContext types per test: EF Core caches the model per CLR type, so sharing one type
    // between tests that pass different collection names would make only the first test's
    // OnModelCreating (and collection name) take effect.
    public class SeededDbContext(DbContextOptions<SeededDbContext> options, string collectionName)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SeedEntity>().ToCouchbaseCollection(this, collectionName);
    }

    public class SyncOnlySeededDbContext(DbContextOptions<SyncOnlySeededDbContext> options, string collectionName)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SeedEntity>().ToCouchbaseCollection(this, collectionName);
    }
}
