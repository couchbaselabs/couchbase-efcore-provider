using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves <see cref="CouchbaseDocumentKeyFormat.V2"/> end to end: composite keys that collide under
/// the legacy format (<c>("x_y","z")</c> and <c>("y","z_x")</c> both give <c>z_x_y</c>) are stored as
/// separate documents under the escaped keys, and updates and deletes find their own document.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DocumentKeyFormatV2Tests(BloggingFixture fixture)
{
    [Fact]
    public async Task V2_CollidingLegacyKeys_AreSeparateDocuments_AndUpdateAndDeleteTargetTheirOwn()
    {
        var collectionName = "keyv2" + Guid.NewGuid().ToString("N");
        var optionsBuilder = new DbContextOptionsBuilder<KeyV2DbContext>();
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
                o.UseDocumentKeyFormat(CouchbaseDocumentKeyFormat.V2);
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        try
        {
            await using (var writer = new KeyV2DbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new KeyV2Entity { Left = "x_y", Right = "z", Value = "first" });
                writer.Add(new KeyV2Entity { Left = "y", Right = "z_x", Value = "second" });
                await writer.SaveChangesAsync();
            }

            await using (var reader = new KeyV2DbContext(optionsBuilder.Options, collectionName))
            {
                var all = await PollingHelper.PollForResultAsync(
                    () => reader.Set<KeyV2Entity>().AsNoTracking().ToListAsync(),
                    result => result.Count >= 2,
                    TimeSpan.FromSeconds(10));
                Assert.Equal(["first", "second"], all.Select(e => e.Value).Order().ToArray());
            }

            // The document ids are the escaped V2 keys, not the colliding legacy "z_x_y".
            var ids = await QueryDocumentIdsAsync(collectionName);
            Assert.Equal([@"x\_y_z", @"y_z\_x"], ids.Order().ToArray());

            await using (var updater = new KeyV2DbContext(optionsBuilder.Options, collectionName))
            {
                var first = (await updater.Set<KeyV2Entity>().ToListAsync()).Single(e => e.Value == "first");
                first.Value = "first-updated";
                await updater.SaveChangesAsync();
            }

            await using (var deleter = new KeyV2DbContext(optionsBuilder.Options, collectionName))
            {
                var second = (await deleter.Set<KeyV2Entity>().ToListAsync()).Single(e => e.Value == "second");
                deleter.Remove(second);
                await deleter.SaveChangesAsync();
            }

            await using (var reader = new KeyV2DbContext(optionsBuilder.Options, collectionName))
            {
                var remaining = await reader.Set<KeyV2Entity>().AsNoTracking().ToListAsync();
                Assert.Equal("first-updated", Assert.Single(remaining).Value);
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    // A raw SDK query right after a collection is created can fail until the query service has
    // seen the new keyspace (the EF contexts under test already retry/poll), so retry that.
    private async Task<List<string>> QueryDocumentIdsAsync(string collectionName)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password);
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                var result = await cluster.QueryAsync<string>(
                    $"SELECT RAW META(d).id FROM `{fixture.BucketName}`.`{fixture.ScopeName}`.`{collectionName}` AS d",
                    new global::Couchbase.Query.QueryOptions().ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus));
                var ids = new List<string>();
                await foreach (var id in result.Rows)
                {
                    ids.Add(id);
                }

                if (ids.Count >= 2 || DateTime.UtcNow >= deadline)
                {
                    return ids;
                }
            }
            catch (global::Couchbase.CouchbaseException) when (DateTime.UtcNow < deadline)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
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

    public class KeyV2Entity
    {
        public string Left { get; set; } = string.Empty;
        public string Right { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class KeyV2DbContext(DbContextOptions<KeyV2DbContext> options, string collectionName) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<KeyV2Entity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => new { e.Left, e.Right });
            });
    }
}
