using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Verifies <see cref="Couchbase.EntityFrameworkCore.Metadata.Conventions.CouchbaseMetaConvention"/>
/// sets the <c>Couchbase:MetaField</c> annotation for a correctly-typed property and fails fast
/// when <see cref="CouchbaseMetaAttribute"/> is misapplied to the wrong CLR type, rather than
/// silently writing an annotation
/// <c>Couchbase.EntityFrameworkCore.Query.Internal.CouchbaseQuerySqlGenerator</c> would just ignore.
/// </summary>
public class CouchbaseMetaConventionTests
{
    [Fact]
    public void CasAttribute_OnUlongProperty_SetsAnnotationAndValueGenerated()
    {
        using var ctx = CreateContext();

        var property = ctx.Model.FindEntityType(typeof(ValidEntity))!.FindProperty(nameof(ValidEntity.Cas))!;

        Assert.Equal("Cas", property.FindAnnotation(CouchbaseMetaAnnotationNames.MetaField)?.Value);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
    }

    [Fact]
    public void CasAttribute_OnNonUlongProperty_ThrowsAtModelBuildTime()
    {
        using var ctx = CreateInvalidContext();

        var exception = Assert.Throws<InvalidOperationException>(() => ctx.Model);

        Assert.Contains("[CouchbaseMeta(Cas)]", exception.Message);
        Assert.Contains("ulong", exception.Message);
    }

    private static ValidContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<ValidContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new ValidContext(builder.Options);
    }

    private static InvalidContext CreateInvalidContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<InvalidContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new InvalidContext(builder.Options);
    }

    private class ValidEntity
    {
        public int Id { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Cas)]
        public ulong Cas { get; set; }
    }

    private class ValidContext(DbContextOptions<ValidContext> options) : DbContext(options)
    {
        public DbSet<ValidEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ValidEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "validMetaPost");
                b.HasKey(p => p.Id);
                b.Property(p => p.Cas).IsConcurrencyToken();
            });
        }
    }

    private class InvalidEntity
    {
        public int Id { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Cas)]
        public long NotAUlong { get; set; }
    }

    private class InvalidContext(DbContextOptions<InvalidContext> options) : DbContext(options)
    {
        public DbSet<InvalidEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvalidEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "invalidMetaPost");
                b.HasKey(p => p.Id);
            });
        }
    }
}
