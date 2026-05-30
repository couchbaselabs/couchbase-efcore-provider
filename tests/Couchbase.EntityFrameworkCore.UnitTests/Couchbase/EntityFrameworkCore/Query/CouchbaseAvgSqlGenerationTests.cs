using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies that <c>CouchbaseQuerySqlGenerator.VisitSqlFunction</c> strips the
/// <c>TONUMBER()</c> wrapper that EF Core injects around integer-column arguments
/// to <c>AVG()</c> (NCBC-3891).
///
/// <para>
/// EF Core promotes the operand of <c>Average()</c> on an <c>int</c> column to
/// <c>double</c> via an <c>ExpressionType.Convert</c> node.  The base
/// <c>QuerySqlGenerator</c> turns that into <c>TONUMBER()</c>.  N1QL's
/// <c>AVG()</c> already returns a <c>double</c> for any numeric input, so the
/// <c>TONUMBER()</c> call is redundant and can trigger a
/// <c>CouchbaseParsingException</c> on some server versions.
/// </para>
/// <para>
/// The fix: at the top of <c>VisitSqlFunction</c>, detect the pattern
/// <c>AVG(TONUMBER(…))</c> and emit <c>AVG(…)</c> directly, bypassing the
/// outer <c>TONUMBER</c> call.
/// </para>
/// </summary>
public class CouchbaseAvgSqlGenerationTests
{
    // ---------------------------------------------------------------
    // AVG on int column — the TONUMBER regression (NCBC-3891)
    // ---------------------------------------------------------------

    [Fact]
    public void Average_OnIntColumn_DoesNotEmitTonumber()
    {
        // Arrange — GroupBy + Average on an int column; EF Core injects Convert→double
        using var ctx = CreateContext();
        var query = ctx.Posts
            .GroupBy(p => p.AuthorId)
            .Select(g => new { g.Key, Avg = g.Average(p => p.Rating) });

        // Act
        var sql = query.ToQueryString();

        // Assert — TONUMBER must not appear anywhere inside the AVG call
        Assert.DoesNotContain("TONUMBER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Average_OnIntColumn_EmitsAvgFunction()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts
            .GroupBy(p => p.AuthorId)
            .Select(g => new { g.Key, Avg = g.Average(p => p.Rating) });

        var sql = query.ToQueryString();

        // AVG( must appear in the generated SQL
        Assert.Contains("AVG(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Average_OnIntColumn_ColumnNameAppearsInsideAvg()
    {
        // The operand of AVG should be the raw column reference, not a function call.
        using var ctx = CreateContext();
        var query = ctx.Posts
            .GroupBy(p => p.AuthorId)
            .Select(g => new { g.Key, Avg = g.Average(p => p.Rating) });

        var sql = query.ToQueryString();

        // `rating` (backtick-delimited) must appear — proving the column reference survived
        Assert.Contains("`rating`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Average_OnDoubleColumn_EmitsAvgWithoutTonumber()
    {
        // A double column has no Convert injection, but the guard must still work correctly.
        using var ctx = CreateContext();
        var query = ctx.Posts
            .GroupBy(p => p.AuthorId)
            .Select(g => new { g.Key, Avg = g.Average(p => p.Score) });

        var sql = query.ToQueryString();

        Assert.Contains("AVG(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TONUMBER", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static PostContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<PostContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new PostContext(builder.Options);
    }

    // ---------------------------------------------------------------
    // Test models
    // ---------------------------------------------------------------

    private class Post
    {
        public int    PostId   { get; set; }
        public int    AuthorId { get; set; }
        public int    Rating   { get; set; }   // int → triggers Convert→double in AVG
        public double Score    { get; set; }   // double → no Convert injection
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
