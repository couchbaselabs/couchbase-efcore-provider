using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Extensions;

/// <summary>
/// Unit tests for <see cref="EntityTypeExtensions.GetPrimaryKey(Microsoft.EntityFrameworkCore.Metadata.IEntityType,object)"/>
/// and <see cref="EntityTypeExtensions.GetPrimaryKey(Microsoft.EntityFrameworkCore.Metadata.IEntityType,IUpdateEntry)"/>.
/// </summary>
public class EntityTypeExtensionsTests
{
    // -----------------------------------------------------------------------
    // CLR-instance overload — GetPrimaryKey(IEntityType, object)
    // -----------------------------------------------------------------------

    private class SingleKeyEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class CompositeKeyEntity
    {
        public int TenantId { get; set; }
        public int UserId { get; set; }
        public string Value { get; set; } = "";
    }

    private class StringKeyEntity
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType BuildEntityType<T>(
        Action<Microsoft.EntityFrameworkCore.ModelBuilder> configure)
        where T : class
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<TestContext<T>>();
        builder.UseCouchbaseProvider(opts);

        using var ctx = new TestContext<T>(builder.Options, configure);
        return ctx.Model.FindEntityType(typeof(T))!;
    }

    private class TestContext<T>(
        DbContextOptions options,
        Action<ModelBuilder> configure) : DbContext(options)
        where T : class
    {
        public DbSet<T> Entities { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    }

    [Fact]
    public void GetPrimaryKey_SingleIntKey_ReturnsId()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
        {
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => e.Id);
            });
        });

        var entity = new SingleKeyEntity { Id = 42, Name = "test" };
        Assert.Equal("42", entityType.GetPrimaryKey(entity));
    }

    [Fact]
    public void GetPrimaryKey_StringKey_ReturnsCode()
    {
        var entityType = BuildEntityType<StringKeyEntity>(mb =>
        {
            mb.Entity<StringKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => e.Code);
            });
        });

        var entity = new StringKeyEntity { Code = "ABC", Description = "desc" };
        Assert.Equal("ABC", entityType.GetPrimaryKey(entity));
    }

    [Fact]
    public void GetPrimaryKey_CompositeKey_ReturnsConcatenatedWithUnderscore()
    {
        var entityType = BuildEntityType<CompositeKeyEntity>(mb =>
        {
            mb.Entity<CompositeKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => new { e.TenantId, e.UserId });
            });
        });

        var entity = new CompositeKeyEntity { TenantId = 1, UserId = 7, Value = "x" };
        var key = entityType.GetPrimaryKey(entity);

        // Composite key joins with underscore; order follows key property declaration.
        Assert.Contains("1", key);
        Assert.Contains("7", key);
        Assert.Contains("_", key);
    }

    [Fact]
    public void GetPrimaryKey_DifferentInstances_ProduceDifferentKeys()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
        {
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => e.Id);
            });
        });

        var key1 = entityType.GetPrimaryKey(new SingleKeyEntity { Id = 1 });
        var key2 = entityType.GetPrimaryKey(new SingleKeyEntity { Id = 2 });
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetPrimaryKey_ZeroValue_ReturnsZeroString()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
        {
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => e.Id);
            });
        });

        var key = entityType.GetPrimaryKey(new SingleKeyEntity { Id = 0 });
        Assert.Equal("0", key);
    }

    // -----------------------------------------------------------------------
    // IUpdateEntry overload — GetPrimaryKey(IEntityType, IUpdateEntry)
    // Used for shared entity types (HasMany/WithMany hidden join tables)
    // whose PK columns are shadow properties.
    // -----------------------------------------------------------------------

    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType BuildSharedEntityType()
    {
        // Simulate the hidden join entity created by HasMany/WithMany.
        // Its PK is composite (PostsPostId + TagsTagId), both shadow properties.
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<SkipNavContext>();
        builder.UseCouchbaseProvider(opts);
        using var ctx = new SkipNavContext(builder.Options);
        return ctx.Model.GetEntityTypes()
            .First(e => e.HasSharedClrType);
    }

    private class Post
    {
        public int PostId { get; set; }
        public ICollection<Tag> Tags { get; set; } = [];
    }

    private class Tag
    {
        public string TagId { get; set; } = "";
        public ICollection<Post> Posts { get; set; } = [];
    }

    private class SkipNavContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;
        public DbSet<Tag> Tags  { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "post");
                b.HasKey(p => p.PostId);
                b.HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity("PostTag");
            });
            modelBuilder.Entity<Tag>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "tag");
                b.HasKey(t => t.TagId);
            });
        }
    }

    private static IUpdateEntry MockUpdateEntry(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        params (string propertyName, object value)[] values)
    {
        var mock = new Mock<IUpdateEntry>();
        mock.Setup(e => e.EntityType).Returns(entityType);
        foreach (var (name, value) in values)
        {
            var prop = entityType.FindProperty(name)!;
            mock.Setup(e => e.GetCurrentValue(prop)).Returns(value);
        }
        return mock.Object;
    }

    [Fact]
    public void GetPrimaryKey_UpdateEntry_SingleValue_ReturnsValue()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
        {
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => e.Id);
            });
        });

        var entry = MockUpdateEntry(entityType, ("Id", 99));
        Assert.Equal("99", entityType.GetPrimaryKey(entry));
    }

    [Fact]
    public void GetPrimaryKey_UpdateEntry_CompositeKey_ReturnsConcatenated()
    {
        var entityType = BuildEntityType<CompositeKeyEntity>(mb =>
        {
            mb.Entity<CompositeKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entity");
                b.HasKey(e => new { e.TenantId, e.UserId });
            });
        });

        var entry = MockUpdateEntry(entityType, ("TenantId", 3), ("UserId", 7));
        var key = entityType.GetPrimaryKey(entry);

        Assert.Contains("3", key);
        Assert.Contains("7", key);
        Assert.Contains("_", key);
    }

    [Fact]
    public void GetPrimaryKey_UpdateEntry_SharedEntityType_ReadsShadowProperties()
    {
        // This is the critical case: the hidden join entity for HasMany/WithMany
        // has shadow-only PK properties. The IUpdateEntry overload must read them
        // via GetCurrentValue rather than CLR PropertyInfo (which would return empty).
        var entityType = BuildSharedEntityType();
        Assert.True(entityType.HasSharedClrType,
            "Expected a shared entity type (hidden join table).");

        // PK property names follow EF Core's convention: {NavName}{EntityPk}
        var pkProps = entityType.FindPrimaryKey()!.Properties;
        var entries = pkProps.Select(p => (p.Name, (object)(p.ClrType == typeof(int) ? 1 : "general")))
                             .ToArray();

        var entry = MockUpdateEntry(entityType, entries);
        var key = entityType.GetPrimaryKey(entry);

        Assert.NotEmpty(key);
        // Key must contain values from both shadow FK columns.
        Assert.All(entries, e => Assert.Contains(e.Item2.ToString()!, key));
    }

    [Fact]
    public void GetPrimaryKey_UpdateEntry_SharedEntityType_DifferentValues_ProduceDifferentKeys()
    {
        var entityType = BuildSharedEntityType();
        var pkProps = entityType.FindPrimaryKey()!.Properties;

        // Two join rows: (1, "general") and (1, "informative")
        var entries1 = pkProps.Select(p => (p.Name, (object)(p.ClrType == typeof(int) ? 1 : "general"))).ToArray();
        var entries2 = pkProps.Select(p => (p.Name, (object)(p.ClrType == typeof(int) ? 1 : "informative"))).ToArray();

        var key1 = entityType.GetPrimaryKey(MockUpdateEntry(entityType, entries1));
        var key2 = entityType.GetPrimaryKey(MockUpdateEntry(entityType, entries2));

        Assert.NotEqual(key1, key2);
    }
}
