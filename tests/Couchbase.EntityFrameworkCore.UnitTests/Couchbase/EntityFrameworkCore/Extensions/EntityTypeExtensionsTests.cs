using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

// The tests call the provider's internal key builders, which EF Core flags as internal API.
#pragma warning disable EF1001

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Extensions;

/// <summary>
/// Tests for the <see cref="CouchbaseDocumentKeyFormat.Legacy"/> document key:
/// <see cref="EntityTypeExtensions.GetLegacyPrimaryKey"/> (the save path) and the obsolete public
/// <c>GetPrimaryKey(IEntityType, object)</c>.
/// </summary>
public class EntityTypeExtensionsTests
{
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

    private class BytesKeyEntity
    {
        public byte[] Id { get; set; } = [];
        public string Name { get; set; } = "";
    }

    private class StringKeyEntity
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
    }

    // Declared Value, B, A so that the legacy (reverse CLR declaration) order is A, B: the
    // opposite of the HasKey order B, A.
    private class ReversedCompositeKeyEntity
    {
        public string Value { get; set; } = "";
        public int B { get; set; }
        public int A { get; set; }
    }

    private class ShadowKeyEntity
    {
        public string Name { get; set; } = "";
    }

    private class FieldKeyEntity
    {
#pragma warning disable CS0649 // Set by EF Core through the field mapping.
        public int Id;
#pragma warning restore CS0649
        public string Name { get; set; } = "";
    }

    private class PartlyShadowCompositeKeyEntity
    {
        public int Visible { get; set; }
    }

    private class InternalKeyEntity
    {
        internal int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class KeyBase
    {
        public int TenantId { get; set; }
    }

    private class DerivedKeyEntity : KeyBase
    {
        public int UserId { get; set; }
        public string Name { get; set; } = "";
    }

    // -----------------------------------------------------------------------
    // Model helpers. EF Core caches the model per context type, so each entity type must always be
    // configured the same way: use the one helper per type below.
    // -----------------------------------------------------------------------

    private static IEntityType BuildEntityType<T>(Action<EntityTypeBuilder<T>> configureKey)
        where T : class
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<TestContext<T>>();
        builder.UseCouchbaseProvider(opts);

        using var ctx = new TestContext<T>(builder.Options, mb => mb.Entity<T>(b =>
        {
            b.ToCouchbaseCollection("bucket", "scope", "entity");
            configureKey(b);
        }));
        return ctx.Model.FindEntityType(typeof(T))!;
    }

    private class TestContext<T>(DbContextOptions options, Action<ModelBuilder> configure) : DbContext(options)
        where T : class
    {
        public DbSet<T> Entities { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    }

    private static IEntityType SingleKeyType() => BuildEntityType<SingleKeyEntity>(b => b.HasKey(e => e.Id));

    private static IEntityType StringKeyType() => BuildEntityType<StringKeyEntity>(b => b.HasKey(e => e.Code));

    private static IEntityType CompositeKeyType()
        => BuildEntityType<CompositeKeyEntity>(b => b.HasKey(e => new { e.TenantId, e.UserId }));

    private static IEntityType BytesKeyType() => BuildEntityType<BytesKeyEntity>(b => b.HasKey(e => e.Id));

    private static IEntityType ReversedCompositeKeyType()
        => BuildEntityType<ReversedCompositeKeyEntity>(b => b.HasKey(e => new { e.B, e.A }));

    private static IEntityType ShadowKeyType() => BuildEntityType<ShadowKeyEntity>(b =>
    {
        b.Property<int>("Id");
        b.HasKey("Id");
    });

    private static IEntityType FieldKeyType() => BuildEntityType<FieldKeyEntity>(b => b.HasKey(e => e.Id));

    private static IEntityType PartlyShadowCompositeKeyType() => BuildEntityType<PartlyShadowCompositeKeyEntity>(b =>
    {
        b.Property<int>("Hidden");
        b.HasKey("Hidden", nameof(PartlyShadowCompositeKeyEntity.Visible));
    });

    private static IEntityType InternalKeyType() => BuildEntityType<InternalKeyEntity>(b => b.HasKey(e => e.Id));

    private static IEntityType DerivedKeyType()
        => BuildEntityType<DerivedKeyEntity>(b => b.HasKey(e => new { e.UserId, e.TenantId }));

    // The hidden join entity that HasMany().WithMany() creates. Its key parts are indexer properties
    // of a shared Dictionary<string, object> type.
    private static IEntityType SharedJoinType()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<SkipNavContext>();
        builder.UseCouchbaseProvider(opts);
        using var ctx = new SkipNavContext(builder.Options);
        return ctx.Model.GetEntityTypes().First(e => e.HasSharedClrType);
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
        public DbSet<Tag> Tags { get; set; } = null!;

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

    private static IUpdateEntry Entry(IEntityType entityType, params (string Name, object? Value)[] values)
    {
        var mock = new Mock<IUpdateEntry>();
        mock.Setup(e => e.EntityType).Returns(entityType);
        foreach (var (name, value) in values)
        {
            mock.Setup(e => e.GetCurrentValue(entityType.FindProperty(name)!)).Returns(value);
        }

        return mock.Object;
    }

    private static string Legacy(IEntityType entityType, object entity, params (string Name, object? Value)[] values)
        => entityType.GetLegacyPrimaryKey(entity, Entry(entityType, values));

    private static void AssertThrowsNotPublic(Action buildKey, string entityName, string propertyName)
    {
        var ex = Assert.Throws<InvalidOperationException>(buildKey);
        Assert.Contains($"'{propertyName}'", ex.Message);
        Assert.Contains(entityName, ex.Message);
        Assert.Contains("V2", ex.Message);
        Assert.Contains("UseDocumentKeyFormat(CouchbaseDocumentKeyFormat.Legacy)", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Key parts that are public properties: the 1.x reverse CLR declaration order
    // -----------------------------------------------------------------------

    [Fact]
    public void Legacy_SingleKey_IsTheValue()
    {
        Assert.Equal("99", Legacy(SingleKeyType(), new SingleKeyEntity(), ("Id", 99)));
        Assert.Equal("0", Legacy(SingleKeyType(), new SingleKeyEntity(), ("Id", 0)));
        Assert.Equal("ABC", Legacy(StringKeyType(), new StringKeyEntity(), ("Code", "ABC")));
    }

    [Fact]
    public void Legacy_CompositeKey_UsesTheReverseClrOrder()
    {
        // Declared TenantId then UserId, so the parts are UserId then TenantId.
        Assert.Equal("7_3", Legacy(CompositeKeyType(), new CompositeKeyEntity(), ("TenantId", 3), ("UserId", 7)));
    }

    [Fact]
    public void Legacy_AllKeyPartsArePublicProperties_KeepsTheReverseClrOrder_NotTheKeyOrder()
    {
        Assert.Equal("1_2", Legacy(ReversedCompositeKeyType(), new ReversedCompositeKeyEntity(), ("A", 1), ("B", 2)));
    }

    [Fact]
    public void Legacy_KeyPartDeclaredOnABaseClass_UsesTheReverseOfGetPropertiesOrder()
    {
        // Type.GetProperties() lists the derived class's properties before the base class's, so the
        // 1.x order puts the base class key part (TenantId) first.
        Assert.Equal(
            ["UserId", "Name", "TenantId"],
            typeof(DerivedKeyEntity).GetProperties().Select(p => p.Name).ToArray());

        Assert.Equal("3_7", Legacy(DerivedKeyType(), new DerivedKeyEntity(), ("TenantId", 3), ("UserId", 7)));
    }

    [Fact]
    public void Legacy_ReadsTheValuesFromTheUpdateEntry_NotTheClrObject()
    {
        // During a save, a store-generated value (a sequence value, or a foreign key set from
        // one) is in the update entry; the CLR object still has the default until EF Core
        // accepts the changes.
        Assert.Equal("1000001", Legacy(SingleKeyType(), new SingleKeyEntity { Id = 0 }, ("Id", 1_000_001)));
    }

    [Fact]
    public void Legacy_ByteArrayKey_IsBase64_AndAnEmptyArrayIsATilde()
    {
        var entityType = BytesKeyType();

        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), Legacy(entityType, new BytesKeyEntity(), ("Id", new byte[] { 1, 2, 3 })));
        Assert.Equal("~", Legacy(entityType, new BytesKeyEntity(), ("Id", Array.Empty<byte>())));
        Assert.NotEqual(
            Legacy(entityType, new BytesKeyEntity(), ("Id", new byte[] { 1 })),
            Legacy(entityType, new BytesKeyEntity(), ("Id", new byte[] { 2 })));
    }

    [Fact]
    public void Legacy_KeylessEntity_Throws()
    {
        var entityType = new Mock<IEntityType>();
        entityType.Setup(e => e.FindPrimaryKey()).Returns((IKey?)null);
        entityType.Setup(e => e.DisplayName()).Returns("KeylessEntity");
        var entry = new Mock<IUpdateEntry>();
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var ex = Assert.Throws<InvalidOperationException>(
            () => entityType.Object.GetLegacyPrimaryKey(new object(), entry.Object));

        Assert.Contains("KeylessEntity", ex.Message);
        Assert.Contains("primary key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Shared-type entities (many-to-many join): declared key order
    // -----------------------------------------------------------------------

    [Fact]
    public void Legacy_SharedEntityType_UsesTheDeclaredKeyOrder_AndTheEntryValues()
    {
        var entityType = SharedJoinType();
        var keyProperties = entityType.FindPrimaryKey()!.Properties;
        var values = keyProperties
            .Select(p => (p.Name, (object?)(p.ClrType == typeof(int) ? 1 : "general")))
            .ToArray();

        var key = Legacy(entityType, new Dictionary<string, object>(), values);

        Assert.Equal(string.Join("_", values.Select(v => v.Item2)), key);
    }

    [Fact]
    public void Legacy_SharedEntityType_DifferentValues_GiveDifferentKeys()
    {
        var entityType = SharedJoinType();
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        (string, object?)[] Values(string tag)
            => keyProperties.Select(p => (p.Name, (object?)(p.ClrType == typeof(int) ? 1 : tag))).ToArray();

        Assert.NotEqual(
            Legacy(entityType, new Dictionary<string, object>(), Values("general")),
            Legacy(entityType, new Dictionary<string, object>(), Values("informative")));
    }

    // -----------------------------------------------------------------------
    // Key parts that are not public properties: Legacy throws, V2 builds the key
    // -----------------------------------------------------------------------

    [Fact]
    public void Legacy_ShadowKey_ThrowsAndPointsToV2()
        => AssertThrowsNotPublic(
            () => Legacy(ShadowKeyType(), new ShadowKeyEntity(), ("Id", 5)), nameof(ShadowKeyEntity), "Id");

    [Fact]
    public void Legacy_FieldMappedKey_ThrowsAndPointsToV2()
        => AssertThrowsNotPublic(
            () => Legacy(FieldKeyType(), new FieldKeyEntity(), ("Id", 8)), nameof(FieldKeyEntity), "Id");

    [Fact]
    public void Legacy_CompositeKeyWithAShadowPart_ThrowsAndNamesThatPart()
        => AssertThrowsNotPublic(
            () => Legacy(PartlyShadowCompositeKeyType(), new PartlyShadowCompositeKeyEntity(), ("Hidden", 3), ("Visible", 4)),
            nameof(PartlyShadowCompositeKeyEntity),
            "Hidden");

    [Fact]
    public void Legacy_InternalKeyProperty_ThrowsAndPointsToV2()
        => AssertThrowsNotPublic(
            () => Legacy(InternalKeyType(), new InternalKeyEntity(), ("Id", 5)), nameof(InternalKeyEntity), "Id");

    [Fact]
    public void V2_KeyPartsThatAreNotPublicProperties_BuildTheKey()
    {
        Assert.Equal("5", V2(InternalKeyType(), ("Id", 5)));
        Assert.Equal("5", V2(ShadowKeyType(), ("Id", 5)));
        Assert.Equal("8", V2(FieldKeyType(), ("Id", 8)));
        Assert.Equal("3_4", V2(PartlyShadowCompositeKeyType(), ("Hidden", 3), ("Visible", 4)));

        // The entry must be built from the same IEntityType instance: a second model build can
        // give a new instance, and the mocked entry would then return null.
        static string V2(IEntityType entityType, params (string Name, object? Value)[] values)
            => CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, values), '_');
    }

    // -----------------------------------------------------------------------
    // The empty-key check is in GetDocumentKey, for both formats
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(CouchbaseDocumentKeyFormat.Legacy)]
    [InlineData(CouchbaseDocumentKeyFormat.V2)]
    public void GetDocumentKey_EmptyKey_ThrowsAndNamesTheEntityType(CouchbaseDocumentKeyFormat format)
    {
        var entityType = StringKeyType();

        var ex = Assert.Throws<InvalidOperationException>(() => CouchbaseDatabaseWrapper.GetDocumentKey(
            format, '_', entityType, Entry(entityType, ("Code", "")), new StringKeyEntity()));

        Assert.Contains(nameof(StringKeyEntity), ex.Message);
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void GetDocumentKey_Legacy_GivesTheLegacyKey()
    {
        var entityType = ReversedCompositeKeyType();

        Assert.Equal("1_2", CouchbaseDatabaseWrapper.GetDocumentKey(
            CouchbaseDocumentKeyFormat.Legacy, '|', entityType, Entry(entityType, ("A", 1), ("B", 2)),
            new ReversedCompositeKeyEntity()));
    }

    [Fact]
    public void GetDocumentKey_WithTheDefaultOptions_GivesTheV2Key()
    {
        // No UseDocumentKeyFormat call. Legacy would give "1_2" (reverse CLR order); V2 follows HasKey.
        var options = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");
        var entityType = ReversedCompositeKeyType();

        var key = CouchbaseDatabaseWrapper.GetDocumentKey(
            options.DocumentKeyFormat, options.DocumentKeyDelimiter, entityType,
            Entry(entityType, ("A", 1), ("B", 2)), new ReversedCompositeKeyEntity());

        Assert.Equal("2_1", key);
    }

    // -----------------------------------------------------------------------
    // The obsolete public GetPrimaryKey(IEntityType, object): the Legacy key from the CLR object
    // -----------------------------------------------------------------------

#pragma warning disable CS0618 // These tests cover the obsolete public overload.
    [Fact]
    public void GetPrimaryKey_ReturnsTheLegacyKeyFromTheClrObject()
    {
        Assert.Equal("42", SingleKeyType().GetPrimaryKey(new SingleKeyEntity { Id = 42 }));
        Assert.Equal("ABC", StringKeyType().GetPrimaryKey(new StringKeyEntity { Code = "ABC" }));
        Assert.Equal("7_1", CompositeKeyType().GetPrimaryKey(new CompositeKeyEntity { TenantId = 1, UserId = 7 }));
        Assert.Equal("1_2", ReversedCompositeKeyType().GetPrimaryKey(new ReversedCompositeKeyEntity { A = 1, B = 2 }));
    }

    [Fact]
    public void GetPrimaryKey_MatchesTheSavePathLegacyKey()
    {
        var entityType = BytesKeyType();
        byte[] id = [1, 2, 3];

        Assert.Equal(
            Legacy(entityType, new BytesKeyEntity(), ("Id", id)),
            entityType.GetPrimaryKey(new BytesKeyEntity { Id = id }));
        Assert.Equal("~", entityType.GetPrimaryKey(new BytesKeyEntity { Id = [] }));
    }

    [Fact]
    public void GetPrimaryKey_KeyPartThatIsNotAPublicProperty_Throws()
    {
        AssertThrowsNotPublic(
            () => ShadowKeyType().GetPrimaryKey(new ShadowKeyEntity()), nameof(ShadowKeyEntity), "Id");
        AssertThrowsNotPublic(
            () => FieldKeyType().GetPrimaryKey(new FieldKeyEntity()), nameof(FieldKeyEntity), "Id");
    }
#pragma warning restore CS0618
}
