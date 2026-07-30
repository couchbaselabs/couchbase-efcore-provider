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

    [Fact]
    public void Trim_NoArgs_InWherePredicate_TranslatesToTrim()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.Trim() == "x");

        var sql = query.ToQueryString();

        Assert.Contains("trim(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trim_CharArg_InWherePredicate_TranslatesToTrimWithCharacterSet()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.Trim('x') == "y");

        var sql = query.ToQueryString();

        Assert.Contains("trim(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'x'", sql);
    }

    [Fact]
    public void Trim_CharArrayArg_InWherePredicate_TranslatesToTrimWithCharacterSet()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.Trim('x', 'y') == "z");

        var sql = query.ToQueryString();

        Assert.Contains("trim(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'xy'", sql);
    }

    [Fact]
    public void TrimStart_NoArgs_InWherePredicate_TranslatesToLtrim()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.TrimStart() == "x");

        var sql = query.ToQueryString();

        Assert.Contains("ltrim(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrimEnd_NoArgs_InWherePredicate_TranslatesToRtrim()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.TrimEnd() == "x");

        var sql = query.ToQueryString();

        Assert.Contains("rtrim(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StringCompare_ComparedAgainstZeroInWherePredicate_SimplifiesToDirectComparison()
    {
        // EF Core's own QueryOptimizingExpressionVisitor recognizes the
        // `string.Compare(a, b) > 0` shape and rewrites it directly to `a > b` before
        // translation -- no CASE expression is even built for this common shape. This is
        // core EF Core behavior, not anything Couchbase-specific, but it's cheap and
        // valuable to pin down since it's the actual query shape most application code writes.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => string.Compare(p.Title, "abc") > 0);

        var sql = query.ToQueryString();

        Assert.Contains("`b`.`Title` > 'abc'", sql);
        Assert.DoesNotContain("CASE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StringCompareTo_ComparedAgainstZeroInWherePredicate_SimplifiesToDirectComparison()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.CompareTo("abc") == 0);

        var sql = query.ToQueryString();

        Assert.Contains("`b`.`Title` = 'abc'", sql);
        Assert.DoesNotContain("CASE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StringCompare_ProjectedDirectly_TranslatesToCaseWhen()
    {
        // Once the raw int result is actually needed (not just compared against 0), EF Core's
        // base ComparisonTranslator (registered by RelationalMethodCallTranslatorProvider,
        // inherited unmodified here) builds a CaseExpression -- this provider's inherited
        // CaseExpression rendering must produce valid N1QL for this to work, confirmed here.
        using var ctx = CreateContext();
        var query = ctx.Posts.Select(p => string.Compare(p.Title, "abc"));

        var sql = query.ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN `b`.`Title` = 'abc' THEN 0", sql);
        Assert.Contains("WHEN `b`.`Title` > 'abc' THEN 1", sql);
        Assert.Contains("WHEN `b`.`Title` < 'abc' THEN -1", sql);
    }

    [Fact]
    public void StringCompareTo_ProjectedDirectly_TranslatesToCaseWhen()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Select(p => p.Title.CompareTo("abc"));

        var sql = query.ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN `b`.`Title` = 'abc' THEN 0", sql);
        Assert.Contains("WHEN `b`.`Title` > 'abc' THEN 1", sql);
        Assert.Contains("WHEN `b`.`Title` < 'abc' THEN -1", sql);
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
