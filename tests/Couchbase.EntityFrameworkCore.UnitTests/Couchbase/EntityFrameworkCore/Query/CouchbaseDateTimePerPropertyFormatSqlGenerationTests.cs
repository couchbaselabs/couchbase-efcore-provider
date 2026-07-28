using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies the per-property <see cref="DateTimeFormatAttribute"/>/<c>HasDateTimeFormat</c>
/// override: a property configured with its own format must use that format's Go layout in
/// generated SQL++, independently of both the context-wide <c>DateTimeFormat</c> default and any
/// other property on the same entity.
/// </summary>
public class CouchbaseDateTimePerPropertyFormatSqlGenerationTests
{
    [Fact]
    public void Date_WithAttributeOverride_UsesOwnFormat_NotContextDefault()
    {
        using var ctx = CreateAttributeContext();
        var stamp = new DateTime(2026, 1, 1);

        var overriddenSql = ctx.Entities.Where(e => e.ShipDate.Date == stamp).ToQueryString();
        var defaultSql = ctx.Entities.Where(e => e.Published.Date == stamp).ToQueryString();

        Assert.Contains("2006-01-02", overriddenSql);
        Assert.DoesNotContain("2006-01-02T15:04:05.999Z07:00", overriddenSql);

        Assert.Contains("2006-01-02T15:04:05.999Z07:00", defaultSql);
    }

    [Fact]
    public void Date_WithFluentOverride_UsesOwnFormat_NotContextDefault()
    {
        using var ctx = CreateFluentContext();
        var stamp = new DateTime(2026, 1, 1);

        var overriddenSql = ctx.Entities.Where(e => e.ShipDate.Date == stamp).ToQueryString();
        var defaultSql = ctx.Entities.Where(e => e.Published.Date == stamp).ToQueryString();

        Assert.Contains("2006-01-02", overriddenSql);
        Assert.DoesNotContain("2006-01-02T15:04:05.999Z07:00", overriddenSql);

        Assert.Contains("2006-01-02T15:04:05.999Z07:00", defaultSql);
    }

    private static AttributeContext CreateAttributeContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<AttributeContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new AttributeContext(builder.Options);
    }

    private static FluentContext CreateFluentContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<FluentContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new FluentContext(builder.Options);
    }

    private class AttributeEntity
    {
        public int Id { get; set; }

        [DateTimeFormat("yyyy-MM-dd")]
        public DateTime ShipDate { get; set; }

        public DateTime Published { get; set; }
    }

    private class AttributeContext(DbContextOptions<AttributeContext> options) : DbContext(options)
    {
        public DbSet<AttributeEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AttributeEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "attributePost");
                b.HasKey(p => p.Id);
            });
        }
    }

    private class FluentEntity
    {
        public int Id { get; set; }
        public DateTime ShipDate { get; set; }
        public DateTime Published { get; set; }
    }

    private class FluentContext(DbContextOptions<FluentContext> options) : DbContext(options)
    {
        public DbSet<FluentEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FluentEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "fluentPost");
                b.HasKey(p => p.Id);
                b.Property(p => p.ShipDate).HasDateTimeFormat("yyyy-MM-dd");
            });
        }
    }
}
