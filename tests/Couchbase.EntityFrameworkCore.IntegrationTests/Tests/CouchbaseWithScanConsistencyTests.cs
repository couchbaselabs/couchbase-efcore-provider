using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves the per-query <see cref="CouchbaseQueryableExtensions.WithScanConsistency{TEntity}"/>
/// against a real cluster. The context-wide default is <c>NotBounded</c> (the SDK default), under
/// which a query is not guaranteed to see writes the index has not caught up with yet, so a burst
/// of writes followed immediately by a <c>RequestPlus</c> query is a real test: the query must wait
/// for the index and return every document written.
/// </summary>
/// <remarks>
/// One dedicated <c>DbContext</c> type per test, because EF Core caches the compiled model per
/// context type and ignores differing constructor arguments (see
/// <see cref="CouchbaseConsistentWithTests"/> for the failure that taught the same lesson).
/// </remarks>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseWithScanConsistencyTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    private const int DocumentCount = 200;

    [Fact]
    public async Task Linq_NotBoundedContext_WithScanConsistencyRequestPlus_SeesEveryWrite()
    {
        var collectionName = "scanlinq" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<ScanLinqDbContext>();

        await using var ctx = new ScanLinqDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            await WriteDocumentsAsync(ctx);

            await using var readCtx = new ScanLinqDbContext(optionsBuilder.Options, collectionName);
            var query = readCtx.Entities.Where(e => e.Content == "doc").WithScanConsistency(QueryScanConsistency.RequestPlus);
            outputHelper.WriteLine("SQL: " + query.ToQueryString());

            var results = await query.ToListAsync();

            Assert.Equal(DocumentCount, results.Count);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task FromSql_NotBoundedContext_WithScanConsistencyRequestPlus_SeesEveryWrite()
    {
        var collectionName = "scansql" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<ScanFromSqlDbContext>();

        await using var ctx = new ScanFromSqlDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            await WriteDocumentsAsync(ctx);
            await WaitForKeyspaceReadyAsync(collectionName);

            await using var readCtx = new ScanFromSqlDbContext(optionsBuilder.Options, collectionName);
            // Columns are the camelCase stored field names: this context uses
            // UseCamelCaseNamingConvention(), and FromSqlRaw deserializes straight into the entity.
            var sql = $"SELECT d.id, d.content FROM {DelimitKeyspace(collectionName)} AS d";
            var results = await readCtx.Entities
                .FromSqlRaw(sql)
                .WithScanConsistency(QueryScanConsistency.RequestPlus)
                .ToListAsync();

            Assert.Equal(DocumentCount, results.Count);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task AdoNet_NotBoundedContext_CommandScanConsistencyRequestPlus_SeesEveryWrite()
    {
        var collectionName = "scanado" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<ScanAdoNetDbContext>();

        await using var ctx = new ScanAdoNetDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            await WriteDocumentsAsync(ctx);
            await WaitForKeyspaceReadyAsync(collectionName);

            var connection = ctx.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = (global::Couchbase.EntityFrameworkCore.Storage.Internal.CouchbaseCommand)connection.CreateCommand();
            Assert.Equal(QueryScanConsistency.NotBounded, command.ScanConsistency);
            command.ScanConsistency = QueryScanConsistency.RequestPlus;
            // Count the returned rows rather than using SELECT COUNT(*): a bare COUNT(*) was observed to
            // return a stale count even with request_plus (from the raw SDK too, with no provider code
            // involved), whereas row-returning queries honour it.
            command.CommandText = $"SELECT d.id FROM {DelimitKeyspace(collectionName)} AS d";

            await using var reader = await command.ExecuteReaderAsync();
            var count = 0;
            while (await reader.ReadAsync())
            {
                count++;
            }

            Assert.Equal(DocumentCount, count);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task Linq_OptionBeforeWhere_AndBeforeCountAndAny_IsStillApplied()
    {
        // The option applies to the whole query wherever it sits in the chain. Before this was
        // position-independent, an operator that replaced the shaper (predicate-free Any()/Count())
        // silently dropped it and the query ran with the NotBounded context default.
        var collectionName = "scanpos" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<ScanPositionDbContext>();

        await using var ctx = new ScanPositionDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            await WriteDocumentsAsync(ctx);

            await using var readCtx = new ScanPositionDbContext(optionsBuilder.Options, collectionName);
            var listed = await readCtx.Entities
                .WithScanConsistency(QueryScanConsistency.RequestPlus)
                .Where(e => e.Content == "doc")
                .ToListAsync();
            Assert.Equal(DocumentCount, listed.Count);

            // Predicate-free Any() on a collection whose index is still catching up.
            await using var anyCtx = new ScanPositionDbContext(optionsBuilder.Options, collectionName);
            Assert.True(await anyCtx.Entities.WithScanConsistency(QueryScanConsistency.RequestPlus).AnyAsync());

            // A filtered count (a bare COUNT(*) is stale server-side even with request_plus).
            await using var countCtx = new ScanPositionDbContext(optionsBuilder.Options, collectionName);
            var count = await countCtx.Entities
                .Where(e => e.Content == "doc")
                .WithScanConsistency(QueryScanConsistency.RequestPlus)
                .CountAsync();
            Assert.Equal(DocumentCount, count);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task Linq_RequestPlusContext_UnfilteredCountAfterDeleteAndInsert_IsExact()
    {
        // The query service answers a bare COUNT(*) from the collection's document count (a
        // CountScan), which ignores request_plus and lags just after documents are deleted and
        // inserted again. Every round must see exactly the documents just written.
        const int rounds = 20;
        const int documentsPerRound = 4;
        var collectionName = "scancount" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<ScanCountDbContext>(QueryScanConsistency.RequestPlus);

        await using var ctx = new ScanCountDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            await WaitForKeyspaceReadyAsync(collectionName);

            for (var round = 0; round < rounds; round++)
            {
                await DeleteAllDocumentsAsync(collectionName);

                await using (var writeCtx = new ScanCountDbContext(optionsBuilder.Options, collectionName))
                {
                    for (var i = 0; i < documentsPerRound; i++)
                    {
                        writeCtx.Add(new ScanEntity { Id = $"{round}-{i}", Content = "doc" });
                    }

                    await writeCtx.SaveChangesAsync();
                }

                await using var readCtx = new ScanCountDbContext(optionsBuilder.Options, collectionName);
                Assert.Equal(documentsPerRound, await readCtx.Entities.CountAsync());
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private static async Task WriteDocumentsAsync(DbContext ctx)
    {
        for (var i = 0; i < DocumentCount; i++)
        {
            ctx.Add(new ScanEntity { Id = Guid.NewGuid().ToString("N"), Content = "doc" });
        }

        await ctx.SaveChangesAsync();
    }

    private DbContextOptionsBuilder<TContext> CreateOptionsBuilder<TContext>(
        QueryScanConsistency scanConsistency = QueryScanConsistency.NotBounded)
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
                o.AutoCreateIndexes = true;
                o.ScanConsistency = scanConsistency;
            });
        optionsBuilder.UseCamelCaseNamingConvention();
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return optionsBuilder;
    }

    private string DelimitKeyspace(string collectionName) =>
        $"`{fixture.BucketName}`.`{fixture.ScopeName}`.`{collectionName}`";

    // A newly created collection can take a moment to become visible to the query engine.
    private async Task WaitForKeyspaceReadyAsync(string collectionName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                var clusterOptions = new global::Couchbase.ClusterOptions()
                    .WithConnectionString(fixture.Host)
                    .WithPasswordAuthentication(fixture.Username, fixture.Password);
                using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
                var result = await cluster.QueryAsync<int>($"SELECT RAW 1 FROM {DelimitKeyspace(collectionName)} AS d LIMIT 1");
                await foreach (var _ in result.Rows)
                {
                }

                return;
            }
            catch (global::Couchbase.Core.Exceptions.IndexFailureException) when (DateTime.UtcNow < deadline)
            {
                outputHelper.WriteLine($"Keyspace {collectionName} not yet visible to the query engine, retrying...");
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }

    // Deletes every document in the collection, reading the response to the end so the delete has
    // finished before the next round writes.
    private async Task DeleteAllDocumentsAsync(string collectionName)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password);
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        using var result = await cluster.QueryAsync<object>(
            $"DELETE FROM {DelimitKeyspace(collectionName)}",
            new QueryOptions().ScanConsistency(QueryScanConsistency.RequestPlus));
        await foreach (var _ in result.Rows)
        {
        }
    }

    private async Task DropCollectionAsync(string collectionName)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password);
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        try
        {
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class ScanEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class ScanLinqDbContext(DbContextOptions<ScanLinqDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<ScanEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ScanEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class ScanCountDbContext(DbContextOptions<ScanCountDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<ScanEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ScanEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class ScanPositionDbContext(DbContextOptions<ScanPositionDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<ScanEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ScanEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class ScanAdoNetDbContext(DbContextOptions<ScanAdoNetDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<ScanEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ScanEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class ScanFromSqlDbContext(DbContextOptions<ScanFromSqlDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<ScanEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ScanEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }
}
