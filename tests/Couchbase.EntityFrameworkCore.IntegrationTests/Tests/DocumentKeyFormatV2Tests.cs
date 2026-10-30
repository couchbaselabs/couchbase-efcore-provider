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

    [Fact]
    public async Task V2_UnixMillisDateTimeKey_KeepsTheSameDocumentAcrossAReload()
    {
        // A DateTime key stored as unix millis loses sub-millisecond ticks. The key must be built
        // from the stored value, or the reloaded entity would address a different document.
        var collectionName = "keyv2ms" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<KeyV2MillisDbContext>();
        var when = new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Utc).AddTicks(1234);

        try
        {
            await using (var writer = new KeyV2MillisDbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new KeyV2MillisEntity { When = when, Part = 1, Value = "original" });
                await writer.SaveChangesAsync();
            }

            await using (var updater = new KeyV2MillisDbContext(optionsBuilder.Options, collectionName))
            {
                var entity = (await PollingHelper.PollForResultAsync(
                    () => updater.Set<KeyV2MillisEntity>().ToListAsync(),
                    result => result.Count >= 1,
                    TimeSpan.FromSeconds(10))).Single();
                entity.Value = "updated";
                await updater.SaveChangesAsync();
            }

            // Updated in place: one document, not a duplicate under a differently-truncated key.
            await using (var reader = new KeyV2MillisDbContext(optionsBuilder.Options, collectionName))
            {
                Assert.Equal("updated", Assert.Single(await reader.Set<KeyV2MillisEntity>().AsNoTracking().ToListAsync()).Value);
            }

            await using (var deleter = new KeyV2MillisDbContext(optionsBuilder.Options, collectionName))
            {
                deleter.Remove((await deleter.Set<KeyV2MillisEntity>().ToListAsync()).Single());
                await deleter.SaveChangesAsync();
            }

            await using (var reader = new KeyV2MillisDbContext(optionsBuilder.Options, collectionName))
            {
                Assert.Empty(await reader.Set<KeyV2MillisEntity>().AsNoTracking().ToListAsync());
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Theory]
    [InlineData("1234.50")]
    [InlineData("1234.567890123456")]
    public async Task V2_DecimalKey_KeepsTheSameDocumentAcrossAReload(string amountText)
    {
        // 1234.50m is stored as the JSON number 1234.5 and read back as 1234.5m. The key must not
        // keep the trailing zero, or the reloaded entity would address a different document.
        // A 16-digit value must survive too: the reader keeps every digit the server returns.
        var amount = decimal.Parse(amountText, System.Globalization.CultureInfo.InvariantCulture);
        var collectionName = "keyv2dec" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<KeyV2DecimalDbContext>();

        try
        {
            await using (var writer = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new KeyV2DecimalEntity { Amount = amount, Value = "original" });
                await writer.SaveChangesAsync();
            }

            await using (var updater = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                var entity = (await PollingHelper.PollForResultAsync(
                    () => updater.Set<KeyV2DecimalEntity>().ToListAsync(),
                    result => result.Count >= 1,
                    TimeSpan.FromSeconds(10))).Single();
                entity.Value = "updated";
                await updater.SaveChangesAsync();
            }

            await using (var reader = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                Assert.Equal("updated", Assert.Single(await reader.Set<KeyV2DecimalEntity>().AsNoTracking().ToListAsync()).Value);
            }

            await using (var deleter = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                deleter.Remove((await deleter.Set<KeyV2DecimalEntity>().ToListAsync()).Single());
                await deleter.SaveChangesAsync();
            }

            await using (var reader = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                Assert.Empty(await reader.Set<KeyV2DecimalEntity>().AsNoTracking().ToListAsync());
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task V2_DecimalKeyWithMorePrecisionThanAReloadKeeps_IsRejectedAndNothingIsSaved()
    {
        // Such a key reads back rounded (about 17 significant digits), so it would address a
        // different document after a reload. It is rejected instead of silently saved.
        var collectionName = "keyv2decp" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<KeyV2DecimalDbContext>();

        try
        {
            await using (var writer = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new KeyV2DecimalEntity { Amount = 1.0000000000000000000000000001m, Value = "precise" });

                var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => writer.SaveChangesAsync());
                Assert.Contains("precision", ex.Message);
            }

            await using (var reader = new KeyV2DecimalDbContext(optionsBuilder.Options, collectionName))
            {
                Assert.Empty(await reader.Set<KeyV2DecimalEntity>().AsNoTracking().ToListAsync());
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private DbContextOptionsBuilder<TContext> CreateOptionsBuilder<TContext>()
        where TContext : DbContext
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
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
                o.AutoCreateIndexes = true;
                o.UseDocumentKeyFormat(CouchbaseDocumentKeyFormat.V2);
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return optionsBuilder;
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

    public class KeyV2MillisEntity
    {
        public DateTime When { get; set; }
        public int Part { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public class KeyV2MillisDbContext(DbContextOptions<KeyV2MillisDbContext> options, string collectionName) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<KeyV2MillisEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.Property(e => e.When).HasUnixMillisDateTime();
                b.HasKey(e => new { e.When, e.Part });
            });
    }

    public class KeyV2DecimalEntity
    {
        public decimal Amount { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public class KeyV2DecimalDbContext(DbContextOptions<KeyV2DecimalDbContext> options, string collectionName) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<KeyV2DecimalEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Amount);
            });
    }
}
