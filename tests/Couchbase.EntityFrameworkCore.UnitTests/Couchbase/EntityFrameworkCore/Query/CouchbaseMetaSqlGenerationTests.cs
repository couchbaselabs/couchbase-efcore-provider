using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies a <c>[CouchbaseMeta]</c>/<c>HasCouchbaseMeta</c>-annotated property is projected as
/// <c>META(alias).field</c> in generated SQL++, instead of a normal document-field reference.
/// </summary>
public class CouchbaseMetaSqlGenerationTests(ITestOutputHelper output)
{
    [Fact]
    public void Select_CasProperty_ProjectsAsMetaCasWithExplicitAlias()
    {
        // N1QL's own implicit result-key for META(alias).cas does not reliably match the
        // property's real name -- without an explicit AS, the reader would look up the wrong
        // key and silently materialize a default value instead of the real one (this is exactly
        // the shape of a real bug this test guards against; see Select_IdProperty_* below for a
        // case where the property name and META field name are NOT just a casing difference, so
        // a case-insensitive substring match couldn't paper over a missing alias).
        using var ctx = CreateContext();
        var sql = ctx.Entities.Select(e => new { e.Id, e.Cas }).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("META(", sql);
        Assert.Matches(@"META\([^)]+\)\.cas\s+AS\s+`Cas`", sql);
    }

    [Fact]
    public void Select_IdProperty_ProjectsAsMetaIdWithExplicitAlias()
    {
        // Unlike Cas ("Cas" vs "cas" -- a casing difference the reader's case-insensitive alias
        // lookup could coincidentally paper over), DocumentKey's property name and the "id" meta
        // field name share no characters at all, so this only passes if the explicit alias is
        // genuinely present -- the exact scenario that exposed the original aliasing bug
        // (META(d).id materializing into a "DocId" property came back null without it).
        using var ctx = CreateContext();
        var sql = ctx.Entities.Select(e => new { e.Id, e.DocumentKey }).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("META(", sql);
        Assert.Matches(@"META\([^)]+\)\.id\s+AS\s+`DocumentKey`", sql);
    }

    [Fact]
    public void Select_NonMetaProperty_ProjectsAsNormalColumn()
    {
        using var ctx = CreateContext();
        var sql = ctx.Entities.Select(e => e.Id).ToQueryString();

        Assert.DoesNotContain("META(", sql);
    }

    [Fact]
    public void Where_CasProperty_UsesMetaCasInPredicate()
    {
        using var ctx = CreateContext();
        var sql = ctx.Entities.Where(e => e.Cas == 42ul).ToQueryString();

        Assert.Contains("META(", sql);
        Assert.Contains(").cas", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_FlagsProperty_ProjectsAsMetaFlagsWithExplicitAlias()
    {
        using var ctx = CreateContext();
        var sql = ctx.Entities.Select(e => new { e.Id, e.DocFlags }).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("META(", sql);
        Assert.Matches(@"META\([^)]+\)\.flags\s+AS\s+`DocFlags`", sql);
    }

    [Fact]
    public void Select_TypeProperty_ProjectsAsMetaTypeWithExplicitAlias()
    {
        using var ctx = CreateContext();
        var sql = ctx.Entities.Select(e => new { e.Id, e.DocType }).ToQueryString();
        output.WriteLine("SQL: " + sql);

        Assert.Contains("META(", sql);
        Assert.Matches(@"META\([^)]+\)\.type\s+AS\s+`DocType`", sql);
    }

    private static MetaContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<MetaContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new MetaContext(builder.Options);
    }

    private class MetaEntity
    {
        public int Id { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Cas)]
        public ulong Cas { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Id)]
        public string DocumentKey { get; set; } = string.Empty;

        [CouchbaseMeta(CouchbaseMetaField.Flags)]
        public uint DocFlags { get; set; }

        [CouchbaseMeta(CouchbaseMetaField.Type)]
        public string DocType { get; set; } = string.Empty;
    }

    private class MetaContext(DbContextOptions<MetaContext> options) : DbContext(options)
    {
        public DbSet<MetaEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MetaEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "metaPost");
                b.HasKey(p => p.Id);
                b.Property(p => p.Cas).IsConcurrencyToken();
            });
        }
    }
}
