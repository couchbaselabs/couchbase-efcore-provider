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

    [Fact]
    public void UtcNow_DefaultFormat_UsesObservedGoLayoutByteForByte()
    {
        // Regression guard: this exact Go layout string was empirically confirmed (CBEF-23 step-0
        // spike) to match this provider's own default DateTime serialization. The DateTimeFormat
        // refactor must not silently change this default.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Published < DateTime.UtcNow);

        Assert.Contains("2006-01-02T15:04:05.999Z07:00", query.ToQueryString());
    }

    [Fact]
    public void Date_CustomFormat_UsesConfiguredGoLayoutNotDefault()
    {
        using var ctx = CreateContext("yyyy-MM-dd");
        var stamp = new DateTime(2026, 1, 1);
        var query = ctx.Posts.Where(p => p.Published.Date == stamp);

        var sql = query.ToQueryString();
        Assert.Contains("2006-01-02", sql);
        Assert.DoesNotContain("2006-01-02T15:04:05.999Z07:00", sql);
    }

    [Fact]
    public void UtcNow_CustomFormat_UsesConfiguredGoLayoutNotDefault()
    {
        using var ctx = CreateContext("yyyy-MM-dd");
        var query = ctx.Posts.Where(p => p.Published < DateTime.UtcNow);

        var sql = query.ToQueryString();
        Assert.Contains("2006-01-02", sql);
        Assert.DoesNotContain("2006-01-02T15:04:05.999Z07:00", sql);
    }

    [Fact]
    public void NullableDateTime_HasValue_TranslatesToIsNotNull()
    {
        // Handled entirely by EF Core's own core RelationalSqlTranslatingExpressionVisitor,
        // before any provider-specific translator ever runs (it rewrites Nullable<T>.HasValue to
        // IsNotNull(inner)) -- this is a regression guard confirming this provider's existing
        // NotEqual -> "IS NOT NULL" rendering already handles that correctly, not a new feature.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.ArchivedAt.HasValue);

        var sql = query.ToQueryString();
        Assert.Contains("IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullableDateTime_Value_TranslatesSameAsUnderlyingColumn()
    {
        // .Value is a no-op unwrap to the same underlying SqlExpression -- comparing it should
        // generate identical SQL to comparing the nullable property directly.
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);
        var viaValue = ctx.Posts.Where(p => p.ArchivedAt!.Value == stamp).ToQueryString();
        var viaDirect = ctx.Posts.Where(p => p.ArchivedAt == stamp).ToQueryString();

        Assert.Equal(viaDirect, viaValue);
    }

    private static PostContext CreateContext(string? dateTimeFormat = null)
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<PostContext>();
        builder.UseCouchbaseProvider(clusterOptions, dateTimeFormat is null
            ? null
            : o => o.DateTimeFormat = dateTimeFormat);
        return new PostContext(builder.Options);
    }

    private class Post
    {
        public int PostId { get; set; }
        public DateTime Published { get; set; }
        public DateTime? ArchivedAt { get; set; }
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
