using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata.Conventions;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// A generated integer key gets a Couchbase sequence by default (Couchbase has no identity
/// columns), and the key's value is assigned at SaveChanges, so Add needs no I/O.
/// </summary>
public class CouchbaseKeySequenceConventionTests
{
    [Fact]
    public void Int_key_uses_a_sequence_named_after_the_entity()
        => Assert.Equal("BlogSequence", SequenceName(BuildModel(_ => { }), typeof(Blog)));

    [Fact]
    public void Long_key_uses_a_sequence()
        => Assert.Equal("LongKeyedSequence", SequenceName(BuildModel(_ => { }), typeof(LongKeyed)));

    [Fact]
    public void Key_configured_as_never_generated_has_no_sequence()
        => Assert.Null(SequenceName(
            BuildModel(b => b.Entity<Blog>().Property(e => e.Id).ValueGeneratedNever()), typeof(Blog)));

    [Fact]
    public void Explicit_sequence_is_kept()
        => Assert.Equal("my_seq", SequenceName(
            BuildModel(b => b.Entity<Blog>().Property(e => e.Id).UseSequence("my_seq")), typeof(Blog)));

    [Fact]
    public void Key_with_a_custom_value_generator_has_no_sequence()
        => Assert.Null(SequenceName(
            BuildModel(b => b.Entity<Blog>().Property(e => e.Id).HasValueGenerator<IntGenerator>()), typeof(Blog)));

    [Fact]
    public void String_key_has_no_sequence()
        => Assert.Null(SequenceName(BuildModel(_ => { }), typeof(StringKeyed)));

    [Fact]
    public void Composite_key_has_no_sequence()
    {
        var model = BuildModel(b => b.Entity<Blog>().HasKey(e => new { e.Id, e.Name }));
        Assert.All(
            model.FindEntityType(typeof(Blog))!.FindPrimaryKey()!.Properties,
            p => Assert.Null(p.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)));
    }

    [Fact]
    public void Derived_types_share_the_root_sequence()
    {
        var model = BuildModel(b => b.Entity<SpecialBlog>());
        Assert.Equal("BlogSequence", SequenceName(model, typeof(SpecialBlog)));
    }

    [Fact]
    public void TPC_hierarchy_with_a_sequence_key_passes_validation()
    {
        // EF Core's TpcStoreGeneratedIdentityWarning fires for any generated TPC key unless the
        // provider accepts its strategy; configure it to throw so the test fails if it fires.
        var model = BuildModel(
            b =>
            {
                b.Entity<Blog>().UseTpcMappingStrategy();
                b.Entity<SpecialBlog>();
            },
            o => o.ConfigureWarnings(w => w.Throw(RelationalEventId.TpcStoreGeneratedIdentityWarning)));

        Assert.Equal("BlogSequence", SequenceName(model, typeof(SpecialBlog)));
    }

    [Fact]
    public void TPC_hierarchy_with_another_generated_key_still_warns()
    {
        var e = Assert.Throws<InvalidOperationException>(() => BuildModel(
            b =>
            {
                b.Entity<Blog>().UseTpcMappingStrategy();
                b.Entity<Blog>().Property(e => e.Id).HasValueGenerator<IntGenerator>();
                b.Entity<SpecialBlog>();
            },
            o => o.ConfigureWarnings(w => w.Throw(RelationalEventId.TpcStoreGeneratedIdentityWarning))));

        Assert.Contains(RelationalEventId.TpcStoreGeneratedIdentityWarning.Name!, e.Message);
    }

    [Fact]
    public void Sequence_on_a_non_integer_property_is_rejected()
    {
        var e = Assert.Throws<InvalidOperationException>(() => BuildModel(
            b => b.Entity<StringKeyed>().Property(e => e.Id)
                .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, "s")));

        Assert.Contains("not supported", e.Message);
    }

    [Fact]
    public void Add_gives_the_key_a_temporary_value_without_IO()
    {
        // No cluster is reachable; Add must not query the sequence (sync I/O would also throw).
        using var context = new TestContext(Options(), _ => { });
        var entry = context.Add(new Blog { Name = "a" });

        var id = entry.Property(e => e.Id);
        Assert.True(id.IsTemporary);
        Assert.Equal(0, entry.Entity.Id);
    }

    [Theory]
    [InlineData(null, null, "test-bucket", "test-scope")]
    [InlineData("other-bucket.other-scope.blogs", null, "other-bucket", "test-scope")]
    [InlineData(null, "override-scope", "test-bucket", "override-scope")]
    public void Sequence_location_is_the_entity_bucket_and_the_context_scope(
        string? keyspace, string? scopeOverride, string bucket, string scope)
    {
        var model = BuildModel(b =>
        {
            if (keyspace != null)
            {
                var parts = keyspace.Split('.');
                b.Entity<Blog>().ToCouchbaseCollection(parts[0], parts[1], parts[2]);
            }

            if (scopeOverride != null)
            {
                b.Entity<Blog>().Property(e => e.Id)
                    .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation, scopeOverride);
            }
        });

        var property = model.FindEntityType(typeof(Blog))!.FindPrimaryKey()!.Properties[0];
        var location = CouchbaseSequenceLocation.Find(property, "test-bucket", "test-scope");

        Assert.Equal(new CouchbaseSequenceLocation(bucket, scope, "BlogSequence"), location);
    }

    private static string? SequenceName(IModel model, Type type)
        => model.FindEntityType(type)!.FindPrimaryKey()!.Properties[0]
            .FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value as string;

    private static IModel BuildModel(Action<ModelBuilder> configure, Action<DbContextOptionsBuilder>? options = null)
    {
        using var context = new TestContext(Options(options), configure);
        return context.Model;
    }

    private static DbContextOptions Options(Action<DbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>();
        builder.UseCouchbaseProvider(new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password"));
        // Each test configures its own model.
        builder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        configure?.Invoke(builder);
        return builder.Options;
    }

    private sealed class TestContext(DbContextOptions options, Action<ModelBuilder> configure) : DbContext(options)
    {
        public DbSet<Blog> Blogs => Set<Blog>();
        public DbSet<LongKeyed> LongKeyed => Set<LongKeyed>();
        public DbSet<StringKeyed> StringKeyed => Set<StringKeyed>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);

        // A fresh model per context: the tests configure different models for the same CLR types.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, NoCacheKeyFactory>();
    }

    private sealed class NoCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new object();
    }

    private sealed class IntGenerator : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator<int>
    {
        public override bool GeneratesTemporaryValues => false;
        public override int Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) => 42;
    }

    public class Blog
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class SpecialBlog : Blog
    {
        public string? Special { get; set; }
    }

    public class LongKeyed
    {
        public long Id { get; set; }
    }

    public class StringKeyed
    {
        public string Id { get; set; } = "";
    }
}
