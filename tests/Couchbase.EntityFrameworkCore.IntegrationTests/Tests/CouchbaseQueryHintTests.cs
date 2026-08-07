using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Proves N1QL <c>USE INDEX</c>/<c>USE HASH</c> query hints (<see cref="CouchbaseQueryableExtensions"/>)
/// actually execute correctly against a real cluster -- these are optimizer nudges that must not
/// change query results, so the point of these tests is confirming the hint text doesn't break
/// execution (a N1QL syntax error) and results stay correct, not that the planner honored the hint.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseQueryHintTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task UseIndex_WithRealSecondaryIndex_ExecutesAndReturnsCorrectResults()
    {
        var collectionName = "hintidx" + Guid.NewGuid().ToString("N");
        var optionsBuilder = new DbContextOptionsBuilder<HintIndexDbContext>();
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

        await using var ctx = new HintIndexDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Entities.AddRange(
                new HintIndexEntity { Id = 1, Score = 10 },
                new HintIndexEntity { Id = 2, Score = 20 },
                new HintIndexEntity { Id = 3, Score = 30 });
            await ctx.SaveChangesAsync();

            await using var readCtx = new HintIndexDbContext(optionsBuilder.Options, collectionName);
            var sql = readCtx.Entities.UseIndex("ix_hint_score").Where(e => e.Score >= 20).ToQueryString();
            outputHelper.WriteLine("SQL: " + sql);

            var results = await readCtx.Entities.UseIndex("ix_hint_score").Where(e => e.Score >= 20).OrderBy(e => e.Score).ToListAsync();

            Assert.Equal(2, results.Count);
            Assert.Equal(20, results[0].Score);
            Assert.Equal(30, results[1].Score);
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    [Fact]
    public async Task UseHash_OnJoinInnerSequence_ExecutesAndReturnsCorrectResults()
    {
        var collectionName = "hinthash" + Guid.NewGuid().ToString("N");
        var optionsBuilder = new DbContextOptionsBuilder<HintJoinDbContext>();
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

        await using var ctx = new HintJoinDbContext(optionsBuilder.Options, collectionName);
        try
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Authors.Add(new HintAuthor { Id = 1, Name = "Ada" });
            ctx.Posts.Add(new HintPost { Id = 1, AuthorId = 1, Title = "Hello" });
            await ctx.SaveChangesAsync();

            await using var readCtx = new HintJoinDbContext(optionsBuilder.Options, collectionName);
            var query = readCtx.Posts.Join(
                readCtx.Authors.UseHash(CouchbaseHashHintType.Build),
                p => p.AuthorId,
                a => a.Id,
                (p, a) => new { p.Title, a.Name });

            outputHelper.WriteLine("SQL: " + query.ToQueryString());

            var results = await query.ToListAsync();

            Assert.Single(results);
            Assert.Equal("Hello", results[0].Title);
            Assert.Equal("Ada", results[0].Name);
        }
        finally
        {
            await DropCollectionAsync(collectionName, collectionName2: "hintauthor" + collectionName);
        }
    }

    private async Task DropCollectionAsync(string collectionName, string? collectionName2 = null)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithCredentials(fixture.Username, fixture.Password);
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        foreach (var name in new[] { collectionName, collectionName2 })
        {
            if (name == null) continue;
            try
            {
                await bucket.Collections.DropCollectionAsync(fixture.ScopeName, name);
            }
            catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
            {
            }
        }
    }

    public class HintIndexEntity
    {
        public long Id { get; set; }
        public double Score { get; set; }
    }

    public class HintIndexDbContext(DbContextOptions<HintIndexDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<HintIndexEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<HintIndexEntity>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.HasIndex(e => e.Score).HasDatabaseName("ix_hint_score");
            });
        }
    }

    public class HintPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public long AuthorId { get; set; }
    }

    public class HintAuthor
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class HintJoinDbContext(DbContextOptions<HintJoinDbContext> options, string collectionName)
        : DbContext(options)
    {
        public DbSet<HintPost> Posts { get; set; } = null!;
        public DbSet<HintAuthor> Authors { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<HintPost>(b => b.ToCouchbaseCollection(this, collectionName));
            modelBuilder.Entity<HintAuthor>(b => b.ToCouchbaseCollection(this, "hintauthor" + collectionName));
        }
    }
}
