using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies LINQ <see cref="Math"/> method translation to SQL++ (CBEF-23) via the new
/// <c>CouchbaseMathMethodTranslator</c> -- previously nonexistent in this provider (the
/// method-call translator provider only ever registered a string translator; Math/DateTime/etc.
/// translators SQLite ships were left commented out and never implemented for Couchbase).
/// </summary>
public class CouchbaseMathFunctionSqlGenerationTests
{
    [Fact]
    public void Abs_TranslatesToAbs()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Abs(p.Score) > 1);

        Assert.Contains("ABS(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ceiling_TranslatesToCeil()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Ceiling(p.Score) > 1);

        Assert.Contains("CEIL(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Floor_TranslatesToFloor()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Floor(p.Score) > 1);

        Assert.Contains("FLOOR(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Round_NoDigits_TranslatesToRound()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Round(p.Score) > 1);

        Assert.Contains("ROUND(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Round_WithDigits_TranslatesToRoundWithTwoArgs()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Round(p.Score, 2) > 1);

        var sql = query.ToQueryString();
        Assert.Contains("ROUND(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(", 2)", sql);
    }

    [Fact]
    public void Truncate_TranslatesToTrunc()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Truncate(p.Score) > 1);

        Assert.Contains("TRUNC(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pow_TranslatesToPower()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Pow(p.Score, 2) > 1);

        Assert.Contains("POWER(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqrt_TranslatesToSqrt()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Sqrt(p.Score) > 1);

        Assert.Contains("SQRT(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_TranslatesToSign()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Sign(p.Score) > 0);

        Assert.Contains("SIGN(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Log_TranslatesToLn()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Log(p.Score) > 1);

        Assert.Contains("LN(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Log10_TranslatesToLog()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Log10(p.Score) > 1);

        Assert.Contains("LOG(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exp_TranslatesToExp()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Exp(p.Score) > 1);

        Assert.Contains("EXP(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogWithNewBase_TranslatesToChangeOfBaseViaLn()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Log(p.Score, 2) > 1);

        var sql = query.ToQueryString();
        Assert.Contains("LN(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/", sql);
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
        public double Score { get; set; }
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
