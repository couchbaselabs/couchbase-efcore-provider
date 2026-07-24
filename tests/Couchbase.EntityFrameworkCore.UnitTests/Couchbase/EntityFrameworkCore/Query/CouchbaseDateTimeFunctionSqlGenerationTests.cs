using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies LINQ <see cref="DateTime"/> member/method translation to SQL++ (CBEF-23), via the new
/// <c>CouchbaseDateTimeMemberTranslator</c>/<c>CouchbaseDateTimeMethodTranslator</c>.
/// </summary>
public class CouchbaseDateTimeFunctionSqlGenerationTests
{
    [Fact]
    public void Year_TranslatesToDatePartStr()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Published.Year == 2026);

        var sql = query.ToQueryString();
        Assert.Contains("DATE_PART_STR(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'year'", sql);
    }

    [Fact]
    public void Month_TranslatesToDatePartStr()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Published.Month == 3);

        var sql = query.ToQueryString();
        Assert.Contains("DATE_PART_STR(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'month'", sql);
    }

    [Fact]
    public void Date_TranslatesToDateTruncStr()
    {
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);
        var query = ctx.Posts.Where(p => p.Published.Date == stamp);

        var sql = query.ToQueryString();
        Assert.Contains("DATE_TRUNC_STR(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'day'", sql);
    }

    [Fact]
    public void UtcNow_TranslatesToNowUtc()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Published < DateTime.UtcNow);

        Assert.Contains("NOW_UTC(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Now_TranslatesToNowLocal()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Published < DateTime.Now);

        Assert.Contains("NOW_LOCAL(", query.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Today_TranslatesToTruncatedNowUtc()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Published < DateTime.Today);

        var sql = query.ToQueryString();
        Assert.Contains("NOW_UTC(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DATE_TRUNC_STR(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddDays_TranslatesToDateAddStr()
    {
        // DATE_ADD_STR returns the resulting date as a string directly (confirmed against a
        // live cluster) -- no MILLIS_TO_STR/MILLIS_TO_UTC wrapping needed or wanted.
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);
        var query = ctx.Posts.Where(p => p.Published.AddDays(1) == stamp);

        var sql = query.ToQueryString();
        Assert.Contains("DATE_ADD_STR(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'day'", sql);
    }

    [Fact]
    public void AddYears_TranslatesToDateAddStrWithYearPart()
    {
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);
        var query = ctx.Posts.Where(p => p.Published.AddYears(1) == stamp);

        var sql = query.ToQueryString();
        Assert.Contains("DATE_ADD_STR(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'year'", sql);
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
        public DateTime Published { get; set; }
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
