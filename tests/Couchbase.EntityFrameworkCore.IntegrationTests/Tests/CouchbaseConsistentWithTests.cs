using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves per-query read-your-own-writes (<see cref="CouchbaseQueryableExtensions.ConsistentWith{TEntity}"/>,
/// <see cref="CouchbaseDatabaseFacadeExtensions.GetMutationState"/>) actually works against a real
/// cluster, across all three query execution paths (LINQ, FromSql, raw ADO.NET). Every test uses
/// <c>NotBounded</c> scan consistency (the SDK default) on the write context specifically so a
/// write is NOT guaranteed visible to a fresh query without the per-query consistency constraint --
/// the point of these tests is confirming <c>.ConsistentWith(...)</c> makes it visible anyway, not
/// just that the generated SQL/request looks right.
/// </summary>
/// <remarks>
/// Each test uses its OWN dedicated <c>DbContext</c> type (rather than sharing one generic context
/// parameterized by collection name, the way this file's first draft did) -- EF Core caches a
/// compiled model per <c>DbContext</c> TYPE by default, keyed independently of any custom
/// constructor arguments, so four tests sharing one context type would silently reuse whichever
/// one built its model first, pointing every test at the SAME collection regardless of the
/// per-test collection name passed to each instance's constructor (confirmed by a real failure:
/// the "Linq" test's error referenced the "AdoNet" test's collection name). One type per test
/// avoids the collision entirely, matching the same one-type-per-test convention already used by
/// <see cref="CouchbaseQueryHintTests"/>.
/// </remarks>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseConsistentWithTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task Linq_WriteThenConsistentWithQuery_SeesTheWrite()
    {
        var collectionName = "ryowlinq" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<RyowLinqDbContext>();

        await using var ctx = new RyowLinqDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();

            var id = Guid.NewGuid().ToString("N");
            ctx.Entities.Add(new RyowEntity { Id = id, Content = "hello" });
            await ctx.SaveChangesAsync();

            var mutationState = ctx.Database.GetMutationState();
            Assert.NotEmpty(mutationState);

            await using var readCtx = new RyowLinqDbContext(optionsBuilder.Options, collectionName);
            var query = readCtx.Entities.Where(e => e.Id == id).ConsistentWith(mutationState);
            outputHelper.WriteLine("SQL: " + query.ToQueryString());

            var result = await query.SingleOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal("hello", result!.Content);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task FromSql_WriteThenConsistentWithQuery_SeesTheWrite()
    {
        var collectionName = "ryowsql" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<RyowFromSqlDbContext>();

        await using var ctx = new RyowFromSqlDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();

            var id = Guid.NewGuid().ToString("N");
            ctx.Entities.Add(new RyowEntity { Id = id, Content = "from-sql" });
            await ctx.SaveChangesAsync();

            var mutationState = ctx.Database.GetMutationState();

            await using var readCtx = new RyowFromSqlDbContext(optionsBuilder.Options, collectionName);
            await WaitForKeyspaceReadyAsync(collectionName);
            // The keyspace identifier can't be parameterized (it's not a bind value), but the id
            // VALUE goes through FromSqlRaw's own {0} positional-parameter mechanism rather than
            // raw string interpolation -- {{0}} below is an escaped literal that becomes the text
            // "{0}" in the resulting SQL, which FromSqlRaw then recognizes and parameterizes.
            // Each keyspace segment must be delimited SEPARATELY (`bucket`.`scope`.`collection`) --
            // wrapping the whole dotted string in one pair of backticks makes N1QL treat it as a
            // single identifier (a bucket literally named "bucket.scope.collection"), which is the
            // exact "No bucket named ..." 12003 error this used to produce.
            // Columns are referenced by their camelCase STORED field name ("id"/"content"), not
            // the CLR property name -- this DbContext uses UseCamelCaseNamingConvention() (see
            // CreateOptionsBuilder), and the FromSqlRaw path deserializes rows into T DIRECTLY via
            // the Couchbase SDK's own SystemTextJsonSerializer (not this provider's own
            // alias-based materializer), which matches JSON keys against camelCase-converted CLR
            // property names WITHOUT a case-insensitive fallback. A raw SQL column reference that
            // doesn't match the stored casing silently deserializes to that property's CLR default
            // instead of throwing -- confirmed by writing "Id"/"Content" (PascalCase, unconverted)
            // in an earlier draft of this test and observing every property come back blank.
            var sql = $"SELECT d.id, d.content FROM {DelimitKeyspace(collectionName)} AS d WHERE d.id = {{0}}";
            var query = readCtx.Entities
                .FromSqlRaw(sql, id)
                .ConsistentWith(mutationState);

            var results = await query.ToListAsync();

            Assert.Single(results);
            Assert.Equal("from-sql", results[0].Content);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task AdoNet_WriteThenConsistentWithCommand_SeesTheWrite()
    {
        var collectionName = "ryowado" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<RyowAdoNetDbContext>();

        await using var ctx = new RyowAdoNetDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();

            var id = Guid.NewGuid().ToString("N");
            ctx.Entities.Add(new RyowEntity { Id = id, Content = "ado-net" });
            await ctx.SaveChangesAsync();

            var mutationState = ctx.Database.GetMutationState();

            await WaitForKeyspaceReadyAsync(collectionName);

            var connection = ctx.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = (CouchbaseCommand)connection.CreateCommand();
            command.ConsistentWith = mutationState;
            // Columns referenced by their camelCase STORED field name -- see the FromSqlRaw
            // test's comment for why (this DbContext uses UseCamelCaseNamingConvention()).
            command.CommandText = $"SELECT d.id, d.content FROM {DelimitKeyspace(collectionName)} AS d WHERE d.id = $id";
            command.Parameters.AddWithValue("$id", id);

            await using var reader = await command.ExecuteReaderAsync();
            var found = await reader.ReadAsync();

            Assert.True(found);
            Assert.Equal("ado-net", reader.GetString(reader.GetOrdinal("content")));
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task ClearMutationState_ResetsAccumulation()
    {
        var collectionName = "ryowclear" + Guid.NewGuid().ToString("N");
        var optionsBuilder = CreateOptionsBuilder<RyowClearDbContext>();

        await using var ctx = new RyowClearDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();

            ctx.Entities.Add(new RyowEntity { Id = Guid.NewGuid().ToString("N"), Content = "before-clear" });
            await ctx.SaveChangesAsync();
            Assert.NotEmpty(ctx.Database.GetMutationState());

            ctx.Database.ClearMutationState();

            Assert.Empty(ctx.Database.GetMutationState());
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
                o.AutoCreateIndexes = true;
                // Deliberately NotBounded (the SDK default) -- RequestPlus would make the "does
                // ConsistentWith actually matter" question moot by making every write immediately
                // visible on its own.
                o.ScanConsistency = QueryScanConsistency.NotBounded;
            });
        // Needed so the FromSqlRaw/ADO.NET raw-SQL column references (which must match the
        // ACTUAL stored field casing) line up with what gets written -- see the FromSqlRaw
        // test's comment for why this matters for those two paths specifically.
        optionsBuilder.UseCamelCaseNamingConvention();
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return optionsBuilder;
    }

    /// <summary>
    /// Delimits each keyspace segment SEPARATELY: <c>`bucket`.`scope`.`collection`</c>. Wrapping
    /// the whole dotted string in a single pair of backticks instead makes N1QL treat it as ONE
    /// identifier -- a bucket literally named <c>"bucket.scope.collection"</c> -- which is the
    /// root cause of the N1QL 12003 ("No bucket named ...") errors this file used to attribute to
    /// a query-engine keyspace-propagation race. There was no race: that malformed reference could
    /// never succeed, no matter how long the retry loop waited.
    /// </summary>
    private string DelimitKeyspace(string collectionName) =>
        $"`{fixture.BucketName}`.`{fixture.ScopeName}`.`{collectionName}`";

    /// <summary>
    /// Polls a freshly created keyspace with a trivial raw query until the query engine actually
    /// recognizes it, retrying on N1QL error 12003 ("Keyspace not found").
    /// </summary>
    private async Task WaitForKeyspaceReadyAsync(string collectionName)
    {
        // A fresh Cluster.ConnectAsync() call -- with a FRESH ClusterOptions object -- is made on
        // EVERY attempt, not once before the loop. Two distinct bugs were found and fixed getting
        // here: (1) reusing one connection across all retries retries against that SAME
        // connection's own cached bucket/collection manifest forever, which never gets a chance to
        // refresh mid-loop; (2) reusing one ClusterOptions instance across multiple ConnectAsync
        // calls throws ObjectDisposedException on the second call, because ClusterOptions holds a
        // CancellationTokenSource tied to the FIRST connected cluster's own lifecycle, which gets
        // disposed when that cluster is disposed at the end of the first iteration.
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
                    // Draining is enough; an empty (but non-erroring) result also proves the
                    // keyspace is recognized.
                }
                return;
            }
            catch (global::Couchbase.Core.Exceptions.IndexFailureException) when (DateTime.UtcNow < deadline)
            {
                outputHelper.WriteLine($"Keyspace {collectionName} not yet visible to the query engine, retrying with a fresh connection...");
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
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

    // "Value" is a reserved N1QL word (collides with SELECT VALUE syntax) -- named Content instead
    // to avoid needing backtick-escaping everywhere this property is referenced in raw SQL below.
    public class RyowEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class RyowLinqDbContext(DbContextOptions<RyowLinqDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<RyowEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RyowEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class RyowFromSqlDbContext(DbContextOptions<RyowFromSqlDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<RyowEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RyowEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class RyowAdoNetDbContext(DbContextOptions<RyowAdoNetDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<RyowEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RyowEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }

    public class RyowClearDbContext(DbContextOptions<RyowClearDbContext> options, string collectionName) : DbContext(options)
    {
        public DbSet<RyowEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RyowEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasKey(e => e.Id);
            });
        }
    }
}
