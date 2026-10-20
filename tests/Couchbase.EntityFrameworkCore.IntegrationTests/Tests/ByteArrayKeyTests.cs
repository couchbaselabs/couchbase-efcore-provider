using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// An entity keyed by a <c>byte[]</c> used to get the document key <c>"System.Byte[]"</c> whatever its
/// value, so every instance wrote to the same document and each save silently overwrote the last.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class ByteArrayKeyTests(BloggingFixture fixture)
{
    [Fact]
    public async Task EntitiesKeyedByDifferentByteArrays_AreStoredAsSeparateDocuments()
    {
        var collectionName = "bytekey" + Guid.NewGuid().ToString("N");
        var optionsBuilder = new DbContextOptionsBuilder<ByteKeyDbContext>();
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

        try
        {
            await using (var writer = new ByteKeyDbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new ByteKeyEntity { Id = [1, 2, 3], Name = "first" });
                writer.Add(new ByteKeyEntity { Id = [4, 5], Name = "second" });
                await writer.SaveChangesAsync();
            }

            await using (var reader = new ByteKeyDbContext(optionsBuilder.Options, collectionName))
            {
                var all = await PollingHelper.PollForResultAsync(
                    () => reader.Set<ByteKeyEntity>().AsNoTracking().ToListAsync(),
                    result => result.Count >= 2,
                    TimeSpan.FromSeconds(10));

                Assert.Equal(["first", "second"], all.Select(e => e.Name).Order().ToArray());
            }

            // Updating one must hit its own document, leaving the other untouched.
            await using (var updater = new ByteKeyDbContext(optionsBuilder.Options, collectionName))
            {
                var first = (await updater.Set<ByteKeyEntity>().ToListAsync()).Single(e => e.Name == "first");
                first.Name = "first-updated";
                await updater.SaveChangesAsync();
            }

            await using (var reader = new ByteKeyDbContext(optionsBuilder.Options, collectionName))
            {
                var all = await reader.Set<ByteKeyEntity>().AsNoTracking().ToListAsync();
                Assert.Equal(["first-updated", "second"], all.Select(e => e.Name).Order().ToArray());
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
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

    public class ByteKeyEntity
    {
        public byte[] Id { get; set; } = [];
        public string Name { get; set; } = string.Empty;
    }

    public class ByteKeyDbContext(DbContextOptions<ByteKeyDbContext> options, string collectionName) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ByteKeyEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
    }
}
