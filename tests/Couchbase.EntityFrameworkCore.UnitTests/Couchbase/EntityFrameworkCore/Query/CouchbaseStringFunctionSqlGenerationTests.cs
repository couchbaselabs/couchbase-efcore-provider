using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies LINQ string-method translation to SQL++ (CBEF-23), including the
/// <c>IndexOf</c>/<c>CONTAINS</c> fix: <c>CONTAINS</c> returns a boolean, not the integer
/// position <c>string.IndexOf</c> must return. The correct N1QL function is <c>POSITION</c>.
/// </summary>
public class CouchbaseStringFunctionSqlGenerationTests
{
    [Fact]
    public void IndexOf_TranslatesToPosition_NotContains()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.IndexOf("abc") > 0);

        var sql = query.ToQueryString();

        Assert.Contains("POSITION(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONTAINS(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsNullOrEmpty_OnNullableColumn_ChecksIsNullOrEqualToEmptyString()
    {
        // Title must be nullable here: on a non-nullable column EF Core's null-semantics
        // optimizer provably prunes the IS NULL branch, which would make this test pass for the
        // wrong reason (already observed: on a non-nullable column this collapses to `= ''`).
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => string.IsNullOrEmpty(p.NullableTitle));

        var sql = query.ToQueryString();

        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("''", sql);
    }

    [Fact]
    public void PadLeft_InWherePredicate_TranslatesToLpad()
    {
        // A Select-projection PadLeft can silently client-evaluate instead of throwing; a Where
        // predicate cannot -- it must translate or the query throws. Using Where here proves the
        // function genuinely reached SQL++ rather than being materialized client-side.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.PadLeft(10) == "x");

        var sql = query.ToQueryString();

        Assert.Contains("LPAD(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PadRight_InWherePredicate_TranslatesToRpad()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.PadRight(10) == "x");

        var sql = query.ToQueryString();

        Assert.Contains("RPAD(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartsWith_ConstantPattern_TranslatesToLike()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.StartsWith("abc"));

        var sql = query.ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndsWith_ConstantPattern_TranslatesToLike()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.EndsWith("xyz"));

        var sql = query.ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartsWith_ConstantPatternContainingWildcard_EscapesLiteralPercent()
    {
        // A StartsWith("50%") search must not treat the literal '%' as a wildcard.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.StartsWith("50%"));

        var sql = query.ToQueryString();

        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("50!%%", sql);
    }

    [Fact]
    public void StartsWith_NonConstantPattern_EscapesAtRuntimeViaReplace()
    {
        using var ctx = CreateContext();
        var pattern = "abc"; // a local variable, so EF Core parameterizes it, not folds it to a constant
        var query = ctx.Posts.Where(p => p.Title.StartsWith(pattern));

        var sql = query.ToQueryString();

        Assert.Contains("REPLACE(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
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

    private class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; } = null!;
        public string? NullableTitle { get; set; }
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
}
