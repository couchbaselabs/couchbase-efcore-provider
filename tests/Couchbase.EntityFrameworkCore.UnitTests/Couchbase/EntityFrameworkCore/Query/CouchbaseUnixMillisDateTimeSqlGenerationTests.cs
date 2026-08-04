using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <see cref="UnixMillisDateTimeAttribute"/>/<c>HasUnixMillisDateTime</c>: a property so
/// configured translates <c>.Year</c>/<c>.Date</c>/<c>Add*</c> to N1QL's <c>_MILLIS</c> date-function
/// family instead of the default <c>_STR</c> family, and a direct comparison against
/// <see cref="DateTime.UtcNow"/>/<see cref="DateTime.Now"/>/<see cref="DateTime.Today"/> throws at
/// translation time rather than silently comparing a <c>NUMBER</c> against a <c>_STR</c> function's
/// string result (see <see cref="CouchbaseQuerySqlGenerator"/>'s <c>VisitSqlBinary</c> override).
/// </summary>
public class CouchbaseUnixMillisDateTimeSqlGenerationTests
{
    [Fact]
    public void Year_WithUnixMillisAttribute_TranslatesToDatePartMillis()
    {
        using var ctx = CreateContext();
        var sql = ctx.Events.Where(e => e.OccurredAt.Year == 2026).ToQueryString();

        Assert.Contains("DATE_PART_MILLIS(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'year'", sql);
        Assert.DoesNotContain("DATE_PART_STR(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Date_WithUnixMillisAttribute_TranslatesToDateTruncMillis()
    {
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);
        var sql = ctx.Events.Where(e => e.OccurredAt.Date == stamp).ToQueryString();

        Assert.Contains("DATE_TRUNC_MILLIS(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'day'", sql);
        Assert.DoesNotContain("DATE_TRUNC_STR(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddDays_WithUnixMillisAttribute_TranslatesToDateAddMillis()
    {
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);
        var sql = ctx.Events.Where(e => e.OccurredAt.AddDays(1) == stamp).ToQueryString();

        Assert.Contains("DATE_ADD_MILLIS(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'day'", sql);
        Assert.DoesNotContain("DATE_ADD_STR(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Year_WithUnixMillisFluent_TranslatesToDatePartMillis()
    {
        using var ctx = CreateFluentContext();
        var sql = ctx.Events.Where(e => e.OccurredAt.Year == 2026).ToQueryString();

        Assert.Contains("DATE_PART_MILLIS(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegularDateTimeProperty_UnaffectedByUnixMillisOnSiblingProperty()
    {
        using var ctx = CreateContext();
        var sql = ctx.Events.Where(e => e.LoggedAt.Year == 2026).ToQueryString();

        Assert.Contains("DATE_PART_STR(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DATE_PART_MILLIS(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Equality_WithUnixMillisAttribute_ComparesAgainstCapturedConstant_DoesNotThrow()
    {
        using var ctx = CreateContext();
        var stamp = new DateTime(2026, 1, 1);

        var sql = ctx.Events.Where(e => e.OccurredAt == stamp).ToQueryString();

        Assert.Contains("`OccurredAt`", sql);
    }

    [Fact]
    public void ComparisonAgainstUtcNow_WithUnixMillisAttribute_Throws()
    {
        using var ctx = CreateContext();

        var exception = Assert.Throws<NotSupportedException>(
            () => ctx.Events.Where(e => e.OccurredAt > DateTime.UtcNow).ToQueryString());

        Assert.Contains("UnixMillisDateTime", exception.Message);
    }

    [Fact]
    public void ComparisonAgainstNow_WithUnixMillisAttribute_Throws()
    {
        using var ctx = CreateContext();

        Assert.Throws<NotSupportedException>(
            () => ctx.Events.Where(e => e.OccurredAt > DateTime.Now).ToQueryString());
    }

    [Fact]
    public void ComparisonAgainstToday_WithUnixMillisAttribute_Throws()
    {
        using var ctx = CreateContext();

        Assert.Throws<NotSupportedException>(
            () => ctx.Events.Where(e => e.OccurredAt > DateTime.Today).ToQueryString());
    }

    [Fact]
    public void ComparisonAgainstUtcNow_ReversedOperandOrder_StillThrows()
    {
        using var ctx = CreateContext();

        Assert.Throws<NotSupportedException>(
            () => ctx.Events.Where(e => DateTime.UtcNow < e.OccurredAt).ToQueryString());
    }

    [Fact]
    public void ComparisonAgainstUtcNow_WithRegularProperty_DoesNotThrow()
    {
        using var ctx = CreateContext();

        var sql = ctx.Events.Where(e => e.LoggedAt > DateTime.UtcNow).ToQueryString();

        Assert.Contains("NOW_UTC(", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static EventContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<EventContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new EventContext(builder.Options);
    }

    private static FluentEventContext CreateFluentContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<FluentEventContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new FluentEventContext(builder.Options);
    }

    private class Event
    {
        public int Id { get; set; }

        [UnixMillisDateTime]
        public DateTime OccurredAt { get; set; }

        public DateTime LoggedAt { get; set; }
    }

    private class EventContext(DbContextOptions<EventContext> options) : DbContext(options)
    {
        public DbSet<Event> Events { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Event>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "event");
                b.HasKey(e => e.Id);
            });
        }
    }

    private class FluentEvent
    {
        public int Id { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private class FluentEventContext(DbContextOptions<FluentEventContext> options) : DbContext(options)
    {
        public DbSet<FluentEvent> Events { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentEvent>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "fluentEvent");
                b.HasKey(e => e.Id);
                b.Property(e => e.OccurredAt).HasUnixMillisDateTime();
            });
        }
    }
}
