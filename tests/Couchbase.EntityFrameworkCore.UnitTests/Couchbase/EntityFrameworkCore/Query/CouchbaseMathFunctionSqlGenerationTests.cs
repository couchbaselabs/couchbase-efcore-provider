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

    [Fact]
    public void Sin_TranslatesToSin()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Sin(p.Score) > 0);

        Assert.Contains("SIN(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cos_TranslatesToCos()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Cos(p.Score) > 0);

        Assert.Contains("COS(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tan_TranslatesToTan()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Tan(p.Score) > 0);

        Assert.Contains("TAN(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Asin_TranslatesToAsin()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Asin(p.Score) > 0);

        Assert.Contains("ASIN(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acos_TranslatesToAcos()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Acos(p.Score) > 0);

        Assert.Contains("ACOS(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Atan_TranslatesToAtan()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Atan(p.Score) > 0);

        Assert.Contains("ATAN(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Atan2_TranslatesToAtan2()
    {
        // Math.Atan2(y, x) is order-sensitive -- assert the exact argument order rather than just
        // that both operands appear somewhere in the SQL, so a translator bug that swaps y/x would
        // actually fail this test.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Atan2(p.Score, p.OtherScore) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ATAN2(`b`.`Score`, `b`.`OtherScore`)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Min_TranslatesToArrayMinOverArrayLiteral()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Min(p.Score, 1.0) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ARRAY_MIN([", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Score`", sql);
    }

    [Fact]
    public void Max_TranslatesToArrayMaxOverArrayLiteral()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Max(p.Score, 1.0) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ARRAY_MAX([", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Score`", sql);
    }

    [Fact]
    public void Min_BothColumns_TranslatesToArrayMinWithBothColumns()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Min(p.Score, p.OtherScore) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ARRAY_MIN([", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Score`", sql);
        Assert.Contains("`OtherScore`", sql);
    }

    [Fact]
    public void Max_NestedChain_FlattensIntoSingleArrayMaxWithThreeElements()
    {
        // C# only has the 2-arg overload, but EF Core's own core visitor recognizes a chained
        // Math.Max(Math.Max(a, b), c) of the SAME method and flattens it into a single N-ary
        // GenerateGreatest([a, b, c]) call -- confirmed by reading RelationalSqlTranslatingExpressionVisitor's
        // TryFlattenVisit -- rather than nesting ARRAY_MAX(ARRAY_MAX([a,b]), c).
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => Math.Max(Math.Max(p.Score, p.OtherScore), 1.0) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ARRAY_MAX([", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Score`", sql);
        Assert.Contains("`OtherScore`", sql);

        var firstIndex = sql.IndexOf("ARRAY_MAX([", StringComparison.OrdinalIgnoreCase);
        var secondIndex = sql.IndexOf("ARRAY_MAX([", firstIndex + 1, StringComparison.OrdinalIgnoreCase);
        Assert.True(secondIndex < 0, "Expected a single flattened ARRAY_MAX([ call, not nested calls.");
    }

    [Fact]
    public void EfFunctionsGreatest_TranslatesToArrayMaxOverArrayLiteral()
    {
        // Comes for free from the same GenerateGreatest override Math.Max uses -- EF.Functions.Greatest
        // supports an arbitrary number of arguments (not just two), unlike Math.Max.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.Greatest(p.Score, p.OtherScore, 1.0) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ARRAY_MAX([", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Score`", sql);
        Assert.Contains("`OtherScore`", sql);
    }

    [Fact]
    public void EfFunctionsLeast_TranslatesToArrayMinOverArrayLiteral()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.Least(p.Score, p.OtherScore, 1.0) > 0);

        var sql = query.ToQueryString();
        Assert.Contains("ARRAY_MIN([", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Score`", sql);
        Assert.Contains("`OtherScore`", sql);
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
        public double OtherScore { get; set; }
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
