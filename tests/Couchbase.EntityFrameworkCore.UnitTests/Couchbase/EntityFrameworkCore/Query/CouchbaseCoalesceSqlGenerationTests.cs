using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies C#'s <c>??</c> (null-coalescing) translates to N1QL's <c>IFMISSINGORNULL</c>, not EF
/// Core's default builtin <c>COALESCE</c> function -- N1QL has no <c>COALESCE</c> at all, so an
/// unmodified translation would reach the server as invalid SQL++ and fail only at query-execution
/// time. <c>IFMISSINGORNULL</c> is also the semantically correct choice: a Couchbase document field
/// can be genuinely MISSING (absent from the JSON), not just JSON <c>null</c>, and it's the only
/// one of N1QL's null-handling functions that treats both the way C#'s <c>??</c> does.
/// </summary>
public class CouchbaseCoalesceSqlGenerationTests
{
    [Fact]
    public void Coalesce_TwoOperands_TranslatesToIfMissingOrNull_NotCoalesce()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Select(p => p.NullableTitle ?? "default");

        var sql = query.ToQueryString();

        Assert.Contains("IFMISSINGORNULL(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COALESCE(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Coalesce_ChainedThreeOperands_FlattensToOneIfMissingOrNullCall()
    {
        // EF Core's SqlExpressionFactory.Coalesce() flattens a chain (`a ?? b ?? c`) into a
        // single N-ary builtin "COALESCE" SqlFunctionExpression with all 3 arguments, not a
        // binary-nested COALESCE(a, COALESCE(b, c)) -- confirmed empirically here. N1QL's
        // IFMISSINGORNULL also accepts an arbitrary number of arguments, so the same single
        // name-substitution in VisitSqlFunction handles this correctly with no extra flattening
        // logic needed.
        using var ctx = CreateContext();
        var query = ctx.Posts.Select(p => p.NullableTitle ?? p.SecondTitle ?? "default");

        var sql = query.ToQueryString();

        Assert.DoesNotContain("COALESCE(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IFMISSINGORNULL(`b`.`NullableTitle`, `b`.`SecondTitle`, 'default')", sql);
    }

    [Fact]
    public void Coalesce_InWherePredicate_TranslatesToIfMissingOrNull()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => (p.NullableTitle ?? "default") == "x");

        var sql = query.ToQueryString();

        Assert.Contains("IFMISSINGORNULL(", sql, StringComparison.OrdinalIgnoreCase);
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
        public string? NullableTitle { get; set; }
        public string? SecondTitle { get; set; }
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
