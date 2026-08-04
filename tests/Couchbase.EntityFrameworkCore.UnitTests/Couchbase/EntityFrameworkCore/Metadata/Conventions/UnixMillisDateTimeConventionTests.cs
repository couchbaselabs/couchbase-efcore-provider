using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Verifies <see cref="Couchbase.EntityFrameworkCore.Metadata.Conventions.UnixMillisDateTimeConvention"/>
/// fails fast when <see cref="UnixMillisDateTimeAttribute"/> is misapplied to a non-<see cref="DateTime"/>
/// property, rather than silently attaching a converter EF Core's own value-conversion pipeline
/// would then fail on in a much more confusing way at some later point.
/// </summary>
public class UnixMillisDateTimeConventionTests
{
    [Fact]
    public void UnixMillisDateTimeAttribute_OnNonDateTimeProperty_ThrowsAtModelBuildTime()
    {
        using var ctx = CreateContext();

        var exception = Assert.Throws<InvalidOperationException>(() => ctx.Model);

        Assert.Contains("[UnixMillisDateTime]", exception.Message);
        Assert.Contains("DateTime", exception.Message);
    }

    private static InvalidAttributeContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<InvalidAttributeContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new InvalidAttributeContext(builder.Options);
    }

    private class InvalidAttributeEntity
    {
        public int Id { get; set; }

        [UnixMillisDateTime]
        public int NotADateTime { get; set; }
    }

    private class InvalidAttributeContext(DbContextOptions<InvalidAttributeContext> options) : DbContext(options)
    {
        public DbSet<InvalidAttributeEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvalidAttributeEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "invalidUnixMillisPost");
                b.HasKey(p => p.Id);
            });
        }
    }
}
