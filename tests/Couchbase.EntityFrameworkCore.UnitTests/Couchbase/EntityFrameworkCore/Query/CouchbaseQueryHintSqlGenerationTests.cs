using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <c>UseIndex</c>/<c>UseHash</c> render as N1QL's <c>USE INDEX(...)</c>/<c>USE HASH(...)</c>
/// query hints in generated SQL++.
/// </summary>
public class CouchbaseQueryHintSqlGenerationTests(ITestOutputHelper output)
{
    [Fact]
    public void UseIndex_WithName_RendersUseIndexUsingGsi()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.UseIndex("post_title_idx").ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE INDEX(`post_title_idx` USING GSI)", sql);
    }

    [Fact]
    public void UseIndex_WithFtsType_RendersUsingFts()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.UseIndex("post_fts_idx", CouchbaseIndexType.Fts).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE INDEX(`post_fts_idx` USING FTS)", sql);
    }

    [Fact]
    public void UseIndex_WithNullName_RendersUsingGsiWithNoName()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.UseIndex(null).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE INDEX(USING GSI)", sql);
    }

    [Fact]
    public void UseIndex_ComposedWithWhere_StillRendersHint()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.UseIndex("post_title_idx").Where(p => p.Title == "x").ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE INDEX(`post_title_idx` USING GSI)", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void NoHint_DoesNotRenderUseIndex()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.ToQueryString();

        Assert.DoesNotContain("USE INDEX", sql);
    }

    [Fact]
    public void UseIndex_ChainedWithDifferentValue_LastCallWinsWithoutThrowing()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.UseIndex("first_idx").UseIndex("second_idx", CouchbaseIndexType.Fts).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE INDEX(`second_idx` USING FTS)", sql);
        Assert.DoesNotContain("first_idx", sql);
        // Only one USE INDEX clause should appear -- not one per call.
        Assert.Equal(1, CountOccurrences(sql, "USE INDEX"));
    }

    [Fact]
    public void UseIndex_ChainedWithSameValue_DoesNotThrow()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.UseIndex("post_title_idx").UseIndex("post_title_idx").ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE INDEX(`post_title_idx` USING GSI)", sql);
        Assert.Equal(1, CountOccurrences(sql, "USE INDEX"));
    }

    [Fact]
    public void UseHash_OnJoinInnerSequence_RendersUseHashBuild()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Join(
            ctx.Authors.UseHash(CouchbaseHashHintType.Build),
            p => p.AuthorId,
            a => a.Id,
            (p, a) => new { p.Title, a.Name });
        var sql = query.ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE HASH(BUILD)", sql);
    }

    [Fact]
    public void UseHash_WithProbe_RendersUseHashProbe()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Join(
            ctx.Authors.UseHash(CouchbaseHashHintType.Probe),
            p => p.AuthorId,
            a => a.Id,
            (p, a) => new { p.Title, a.Name });
        var sql = query.ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE HASH(PROBE)", sql);
    }

    [Fact]
    public void NoHint_JoinDoesNotRenderUseHash()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Join(ctx.Authors, p => p.AuthorId, a => a.Id, (p, a) => new { p.Title, a.Name });
        var sql = query.ToQueryString();

        Assert.DoesNotContain("USE HASH", sql);
    }

    [Fact]
    public void UseHash_OnJoinInnerSequenceWithPriorWhere_IgnoresHintRatherThanMisplacingIt()
    {
        // A .Where(...) before .UseHash(...) forces EF Core's AddJoin to push the inner sequence's
        // table down into a subquery once it's used as this join's inner side -- if this were
        // annotated anyway, the hint would end up rendered on that subquery's own inner FROM term
        // instead of the outer join's keyspace reference. Confirm it's silently ignored instead.
        using var ctx = CreateContext();
        var query = ctx.Posts.Join(
            ctx.Authors.Where(a => a.Name != "").UseHash(CouchbaseHashHintType.Build),
            p => p.AuthorId,
            a => a.Id,
            (p, a) => new { p.Title, a.Name });
        var sql = query.ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.DoesNotContain("USE HASH", sql);
    }

    [Fact]
    public void UseHash_OnJoinInnerSequenceWithPriorTake_IgnoresHintRatherThanMisplacingIt()
    {
        // Same pushdown hazard as the .Where(...) case above, triggered by .Take(...) instead
        // (EF Core's AddJoin pushes down on Limit/Offset/Distinct/Predicate/GroupBy alike).
        using var ctx = CreateContext();
        var query = ctx.Posts.Join(
            ctx.Authors.Take(1).UseHash(CouchbaseHashHintType.Build),
            p => p.AuthorId,
            a => a.Id,
            (p, a) => new { p.Title, a.Name });
        var sql = query.ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.DoesNotContain("USE HASH", sql);
    }

    [Fact]
    public void UseHash_ChainedWithDifferentValue_LastCallWinsWithoutThrowing()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Join(
            ctx.Authors.UseHash(CouchbaseHashHintType.Build).UseHash(CouchbaseHashHintType.Probe),
            p => p.AuthorId,
            a => a.Id,
            (p, a) => new { p.Title, a.Name });
        var sql = query.ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("USE HASH(PROBE)", sql);
        Assert.DoesNotContain("USE HASH(BUILD)", sql);
        Assert.Equal(1, CountOccurrences(sql, "USE HASH"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static QueryHintContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<QueryHintContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new QueryHintContext(builder.Options);
    }

    private class Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int AuthorId { get; set; }
    }

    private class Author
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class QueryHintContext(DbContextOptions<QueryHintContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;
        public DbSet<Author> Authors { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "hintPost");
                b.HasKey(p => p.Id);
            });
            modelBuilder.Entity<Author>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "hintAuthor");
                b.HasKey(a => a.Id);
            });
        }
    }
}
