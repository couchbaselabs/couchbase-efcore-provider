// Synchronous APIs throw by default (CouchbaseEventId.SyncNotSupported, a warning
// configured as an error by UseCouchbase); an application that ignores or logs the warning gets
// working, blocking synchronous calls. The same in Debug and Release builds.
using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Diagnostics;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

public class SyncIoHotel
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
}

public class SyncIoContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<SyncIoHotel> Hotels => Set<SyncIoHotel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<SyncIoHotel>(b =>
        {
            b.ToCouchbaseCollection(this, "hotel");
            b.HasKey(h => h.Id);
            b.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(h => h.Type).HasColumnName("type");
            b.Property(h => h.Name).HasColumnName("name");
        });
}

public class SyncIoEnsureCreatedContext(DbContextOptions options, string collectionName) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<SyncIoHotel>(b =>
        {
            b.ToCouchbaseCollection(this, collectionName);
            b.HasKey(h => h.Id);
            b.Property(h => h.Id).ValueGeneratedNever();
        });
}

[Collection(CouchbaseTestingCollection.Name)]
public class SyncIoTests(TravelSampleFixture fx) : IAsyncLifetime
{
    private const int Id = 987650030;
    private ICluster _cluster = null!;
    private ICouchbaseCollection _collection = null!;

    public async Task InitializeAsync()
    {
        _cluster = await Cluster.ConnectAsync(new ClusterOptions()
            .WithConnectionString(fx.Host).WithCredentials(fx.Username, fx.Password));
        _collection = (await (await _cluster.BucketAsync(fx.BucketName)).ScopeAsync(fx.ScopeName)).Collection("hotel");
        await _collection.UpsertAsync(Id.ToString(), new { id = Id, type = "hotel", name = "sync" });
    }

    public async Task DisposeAsync()
    {
        try { await _collection.RemoveAsync(Id.ToString()); } catch (DocumentNotFoundException) { }
        _cluster.Dispose();
    }

    private SyncIoContext Ctx(Action<DbContextOptionsBuilder>? configure = null)
    {
        var b = new DbContextOptionsBuilder<SyncIoContext>();
        b.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        b.UseCouchbase(
            new global::Couchbase.ClusterOptions().WithConnectionString(fx.Host).WithPasswordAuthentication(fx.Username, fx.Password),
            o =>
            {
                o.Bucket = fx.BucketName;
                o.Scope = fx.ScopeName;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        configure?.Invoke(b);

        return new SyncIoContext(b.Options);
    }

    private static void OptIn(DbContextOptionsBuilder b)
        => b.ConfigureWarnings(w => w.Ignore(CouchbaseEventId.SyncNotSupported));

    private static void AssertSyncRejected(Action action)
    {
        var e = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains(CouchbaseEventId.SyncNotSupported.Name!, e.Message);
    }

    [Fact]
    public void Sync_calls_throw_by_default()
    {
        using var ctx = Ctx();
        AssertSyncRejected(() => ctx.Hotels.Where(h => h.Id == Id).ToList());
        AssertSyncRejected(() => ctx.Hotels.First(h => h.Id == Id));
        AssertSyncRejected(() => ctx.Database.BeginTransaction());

        AssertSyncRejected(() => ctx.Database.EnsureCreated());
        AssertSyncRejected(() => ctx.Database.GetDbConnection().Open());

        ctx.Hotels.Add(new SyncIoHotel { Id = Id + 1, Type = "hotel", Name = "never saved" });
        AssertSyncRejected(() => ctx.SaveChanges());
    }

    [Fact]
    public async Task Sync_read_on_a_reader_opened_asynchronously_throws_by_default()
    {
        await using var ctx = Ctx();
        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RAW 1";
        await using var reader = await command.ExecuteReaderAsync();

        AssertSyncRejected(() => reader.Read());
    }

    [Fact]
    public async Task Opted_in_EnsureCreated_creates_missing_collections_in_an_existing_bucket()
    {
        // EF's relational EnsureCreated checks Exists() and then HasTables(), which is always true
        // here, so on an existing bucket it would create nothing.
        var collectionName = "syncio" + Guid.NewGuid().ToString("N");
        var seeded = new List<bool>();
        var b = new DbContextOptionsBuilder<SyncIoEnsureCreatedContext>();
        b.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        b.UseCouchbase(
            new global::Couchbase.ClusterOptions().WithConnectionString(fx.Host).WithPasswordAuthentication(fx.Username, fx.Password),
            o =>
            {
                o.Bucket = fx.BucketName;
                o.Scope = fx.ScopeName;
            });
        OptIn(b);
        b.UseSeeding((_, created) => seeded.Add(created));
        var bucket = await _cluster.BucketAsync(fx.BucketName);

        try
        {
            using (var ctx = new SyncIoEnsureCreatedContext(b.Options, collectionName))
            {
                Assert.False(ctx.Database.EnsureCreated()); // the bucket already existed
            }

            var scopes = await bucket.Collections.GetAllScopesAsync();
            Assert.Contains(scopes.Single(sc => sc.Name == fx.ScopeName).Collections, c => c.Name == collectionName);
            Assert.Equal([true], seeded); // UseSeeding runs, told that the collection was created
        }
        finally
        {
            try
            {
                await bucket.Collections.DropCollectionAsync(fx.ScopeName, collectionName);
            }
            catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
            {
            }
        }
    }

    [Fact]
    public async Task Async_calls_are_unaffected()
    {
        await using var ctx = Ctx();
        var hotel = await ctx.Hotels.FirstAsync(h => h.Id == Id);
        hotel.Name = "async";
        await using var tx = await ctx.Database.BeginTransactionAsync();
        await ctx.SaveChangesAsync();
        await tx.CommitAsync();
    }

    [Fact]
    public void Opted_in_sync_calls_work()
    {
        using (var ctx = Ctx(OptIn))
        {
            Assert.Single(ctx.Hotels.Where(h => h.Id == Id).ToList());
            var hotel = ctx.Hotels.First(h => h.Id == Id);
            hotel.Name = "changed synchronously";
            Assert.Equal(1, ctx.SaveChanges());
        }

        using (var ctx = Ctx(OptIn))
        {
            using var tx = ctx.Database.BeginTransaction();
            var hotel = ctx.Hotels.First(h => h.Id == Id);
            Assert.Equal("changed synchronously", hotel.Name);
            hotel.Name = "rolled back";
            ctx.SaveChanges();
            tx.Rollback();
        }

        using (var ctx = Ctx(OptIn))
        {
            Assert.Equal("changed synchronously", ctx.Hotels.First(h => h.Id == Id).Name);
        }
    }

    [Fact]
    public void Opting_in_with_Log_logs_each_blocking_call()
    {
        var logged = new List<string>();
        using var ctx = Ctx(b => b
            .ConfigureWarnings(w => w.Log(CouchbaseEventId.SyncNotSupported))
            .LogTo(logged.Add, [CouchbaseEventId.SyncNotSupported]));

        Assert.Single(ctx.Hotels.Where(h => h.Id == Id).ToList());
        Assert.Single(logged);
        Assert.Contains("ConfigureWarnings(w => w.Ignore(CouchbaseEventId.SyncNotSupported))", logged[0]);
    }
}
