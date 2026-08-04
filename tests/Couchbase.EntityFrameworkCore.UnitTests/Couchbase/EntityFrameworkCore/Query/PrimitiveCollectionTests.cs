using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies LINQ query support for a scalar "primitive collection" property (a plain
/// <c>List&lt;T&gt;</c>/<c>T[]</c> mapped directly to a JSON array field, not via <c>OwnsMany</c>).
/// This provider implements two hooks: <c>TranslatePrimitiveCollection</c>, expanding the array
/// into a rowset via a bare correlated <c>&lt;arrayExpr&gt; AS value</c> FROM-term (no literal
/// <c>UNNEST</c> keyword -- this Couchbase Server version rejects it as a primary/sole FROM-term)
/// (<see cref="Couchbase.EntityFrameworkCore.Query.Internal.CouchbaseUnnestExpression"/>) for
/// order-independent operators (.Where/.Count/.Contains/.Any), and
/// <c>TranslateElementAtOrDefault</c>, rendering N1QL's native <c>arr[i]</c> subscript directly
/// (<see cref="Couchbase.EntityFrameworkCore.Query.Internal.CouchbaseArrayIndexExpression"/>) for
/// indexer/<c>.ElementAt()</c> access -- this Couchbase Server version also rejects <c>AT pos</c>
/// positional-binding syntax entirely, so there's no ordered-rowset fallback available for
/// indexing, unlike SQLite/SqlServer.
/// </summary>
public class PrimitiveCollectionTests
{
    private class Post
    {
        public int PostId { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private class PostContext(DbContextOptions<PostContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "post");
                b.HasKey(p => p.PostId);
            });
        }
    }

    private static PostContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<PostContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new PostContext(builder.Options);
    }

    [Fact]
    public void Indexer_TranslatesToNativeArraySubscript()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Where(p => p.Tags[0] == "x").ToQueryString();

        Assert.Contains("`b`.`Tags`[0] = 'x'", sql);
    }

    [Fact]
    public void ElementAt_WithNonZeroIndex_UsesThatSubscript()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Where(p => p.Tags.ElementAt(3) == "x").ToQueryString();

        Assert.Contains("`b`.`Tags`[3] = 'x'", sql);
    }

    [Fact]
    public void Contains_TranslatesToSubqueryIn_NotArrayLiteralBrackets()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Where(p => p.Tags.Contains("x")).ToQueryString();

        Assert.Contains("'x' IN (", sql);
        Assert.Contains("FROM `b`.`Tags` AS `t`", sql);
        // The subquery-IN shape must use parens, not this provider's array-literal IN brackets.
        Assert.DoesNotContain("IN [", sql);
    }

    [Fact]
    public void Count_TranslatesToCountSubquery()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Where(p => p.Tags.Count > 1).ToQueryString();

        Assert.Contains("SELECT RAW COUNT(*)", sql);
        Assert.Contains("FROM `b`.`Tags` AS `t`", sql);
    }

    [Fact]
    public void AnyWithPredicate_TranslatesToSubqueryIn()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Where(p => p.Tags.Any(t => t == "x")).ToQueryString();

        Assert.Contains("'x' IN (", sql);
        Assert.DoesNotContain("IN [", sql);
    }

    [Fact]
    public void ProjectedElementAt_TranslatesToNativeArraySubscript()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Select(p => p.Tags[0]).ToQueryString();

        Assert.Contains("SELECT `b`.`Tags`[0]", sql);
    }

    [Fact]
    public void WhereComposedBeforeElementAt_FailsTranslation_NotSilentlyWrong()
    {
        // .Where(...).ElementAt(i) over a primitive collection must fail translation outright,
        // not fall back to EF Core's generic OFFSET/LIMIT implementation: that would render as
        // syntactically valid N1QL over this provider's UNNEST-less rendering (GenerateUnnest
        // handles the FROM-clause fine), but there is no AT-alias positional binding to make
        // OFFSET/LIMIT deterministic, so it would silently return a wrong element instead of
        // failing loudly.
        using var ctx = CreateContext();
        Assert.Throws<InvalidOperationException>(
            () => ctx.Posts.Select(p => p.Tags.Where(t => t != "x").ElementAt(0)).ToQueryString());
    }

    [Fact]
    public void OrderByComposedBeforeElementAt_FailsTranslation_NotSilentlyWrong()
    {
        using var ctx = CreateContext();
        Assert.Throws<InvalidOperationException>(
            () => ctx.Posts.Select(p => p.Tags.OrderBy(t => t).ElementAt(0)).ToQueryString());
    }

    [Fact]
    public void ArrayLiteralIn_StillUsesBrackets_NotBrokenByRegression()
    {
        // Control case: an ordinary .Contains() over an in-memory list (not a queryable primitive
        // collection) must still use this provider's array-literal IN syntax -- confirms the
        // GenerateIn subquery-vs-values fix didn't regress the pre-existing, far more common case.
        using var ctx = CreateContext();
        var ids = new[] { 1, 2, 3 };
        var sql = ctx.Posts.Where(p => ids.Contains(p.PostId)).ToQueryString();

        Assert.Contains("IN [$ids1, $ids2, $ids3]", sql);
    }
}
