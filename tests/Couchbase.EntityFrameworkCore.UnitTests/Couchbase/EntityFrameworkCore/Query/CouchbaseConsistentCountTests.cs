using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Without a predicate, the query service answers <c>COUNT(*)</c> from the collection's document
/// count (a <c>CountScan</c>), which ignores the scan consistency and can be stale just after
/// documents are deleted and inserted. When a query needs read-your-writes consistency, an
/// unfiltered <c>Count()</c>/<c>LongCount()</c> gets a predicate that is true for every document, so
/// the count goes through the primary index, which honours it. A <c>NotBounded</c> query keeps the
/// faster <c>CountScan</c>.
/// </summary>
/// <remarks>
/// A top-level <c>Count()</c> cannot be turned into SQL without a cluster, so these tests count in a
/// subquery, which <c>ToQueryString()</c> renders. The subquery refers to the outer row only in its
/// projection, so EF Core translates it as part of the query (an uncorrelated count would run as a
/// query of its own) and it has no predicate. It goes through the same translation and SQL
/// generation as a top-level count.
/// </remarks>
public class CouchbaseConsistentCountTests
{
    private const string IndexedCount = "WHERE META(`b0`).id IS NOT MISSING";

    [Fact]
    public void Count_NotBoundedContext_HasNoPredicate()
    {
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);

        var sql = ctx.Posts.Where(p => ctx.Posts.Select(x => p.Id).Count() > 1).ToQueryString();

        Assert.Contains("SELECT RAW COUNT(*)", sql);
        Assert.DoesNotContain("META(", sql);
    }

    [Fact]
    public void Count_RequestPlusContext_CountsThroughTheIndex()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var sql = ctx.Posts.Where(p => ctx.Posts.Select(x => p.Id).Count() > 1).ToQueryString();

        Assert.Contains(IndexedCount, sql);
    }

    [Fact]
    public void LongCount_RequestPlusContext_CountsThroughTheIndex()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var sql = ctx.Posts.Where(p => ctx.Posts.Select(x => p.Id).LongCount() > 1).ToQueryString();

        Assert.Contains(IndexedCount, sql);
    }

    [Fact]
    public void Count_WithScanConsistencyRequestPlus_OverNotBoundedContext_CountsThroughTheIndex()
    {
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);

        var sql = ctx.Posts
            .Where(p => ctx.Posts.WithScanConsistency(QueryScanConsistency.RequestPlus).Select(x => p.Id).Count() > 1)
            .ToQueryString();

        Assert.Contains(IndexedCount, sql);
    }

    [Fact]
    public void Count_WithScanConsistencyNotBounded_OverRequestPlusContext_HasNoPredicate()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var sql = ctx.Posts
            .Where(p => ctx.Posts.WithScanConsistency(QueryScanConsistency.NotBounded).Select(x => p.Id).Count() > 1)
            .ToQueryString();

        Assert.DoesNotContain("META(", sql);
    }

    [Fact]
    public void Count_ConsistentWith_CountsThroughTheIndex()
    {
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);
        var mutationState = new MutationState();

        var sql = ctx.Posts.Where(p => ctx.Posts.Select(x => p.Id).Count() > 1)
            .ConsistentWith(mutationState)
            .ToQueryString();

        Assert.Contains(IndexedCount, sql);
    }

    [Fact]
    public void Count_WithScanConsistencyAfterTheCount_CountsThroughTheIndex()
    {
        // The hint applies to the whole query, so it also covers a count translated before it.
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);

        var sql = ctx.Posts.Where(p => ctx.Posts.Select(x => p.Id).Count() > 1)
            .WithScanConsistency(QueryScanConsistency.RequestPlus)
            .ToQueryString();

        Assert.Contains(IndexedCount, sql);
    }

    [Fact]
    public void Count_LastHintIsNotBounded_HasNoPredicate()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var sql = ctx.Posts.Where(p => ctx.Posts.Select(x => p.Id).Count() > 1)
            .WithScanConsistency(QueryScanConsistency.NotBounded)
            .ToQueryString();

        Assert.DoesNotContain("META(", sql);
    }

    [Fact]
    public void Count_AfterWhere_RequestPlusContext_KeepsOnlyTheQueryPredicate()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var sql = ctx.Posts.Where(p => ctx.Posts.Where(x => x.Title == "x").Select(x => p.Id).Count() > 1).ToQueryString();

        Assert.Contains("WHERE `b0`.`Title` = 'x'", sql);
        Assert.DoesNotContain("META(", sql);
    }

    [Fact]
    public void Count_WithPredicate_RequestPlusContext_KeepsOnlyTheQueryPredicate()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var sql = ctx.Posts.Where(p => ctx.Posts.Count(x => x.Title == "x" && p.Id > 0) > 1).ToQueryString();

        Assert.Contains("`b0`.`Title` = 'x'", sql);
        Assert.DoesNotContain("META(", sql);
    }

    [Fact]
    public void Select_RequestPlusContext_HasNoIndexPredicate()
    {
        // Only a COUNT gets the predicate; an ordinary query is not changed.
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        Assert.DoesNotContain("META(", ctx.Posts.ToQueryString());
    }

    private static CountContext CreateContext(QueryScanConsistency contextDefault)
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<CountContext>();
        builder.UseCouchbaseProvider(clusterOptions, o => o.ScanConsistency = contextDefault);
        return new CountContext(builder.Options);
    }

    private class Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private class CountContext(DbContextOptions<CountContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "consistentCountPost");
                b.HasKey(p => p.Id);
            });
        }
    }
}
