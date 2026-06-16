using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Tests for <see cref="CouchbaseDatabaseWrapper.HydrateObjectFromEntity"/>.
///
/// The method is <c>internal static</c> and called directly to avoid requiring
/// the full <c>SaveChangesAsync</c> stack (GetPrimaryKey, GetCollectionName, IsOwned, …).
/// </summary>
public class CouchbaseDatabaseWrapperHydrateTests
{
    private static object Hydrate(IUpdateEntry entry, System.Text.Json.JsonNamingPolicy? policy = null)
        => CouchbaseDatabaseWrapper.HydrateObjectFromEntity(entry, policy)!;

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Mock<IProperty> MakeProperty(
        PropertyInfo? propertyInfo,
        FieldInfo? fieldInfo,
        object? value,
        Mock<IUpdateEntry> entryMock)
    {
        var prop = new Mock<IProperty>();
        prop.Setup(p => p.PropertyInfo).Returns(propertyInfo);
        prop.Setup(p => p.FieldInfo).Returns(fieldInfo);
        // IsShadowProperty() is on the IPropertyBase interface; Moq returns false by default
        // which would cause GetColumnName() to be called on a property with no annotation,
        // throwing NullReferenceException.  Set it explicitly from the CLR members.
        var isShadow = propertyInfo == null && fieldInfo == null;
        prop.Setup(p => p.IsShadowProperty()).Returns(isShadow);
        // Wire GetColumnName() via the relational annotation so it short-circuits before
        // reaching GetDefaultColumnName(), which requires a real entity-type table mapping.
        var name = propertyInfo?.Name ?? fieldInfo?.Name ?? "Prop";
        prop.Setup(p => p.Name).Returns(name);
        var annotation = new Mock<IAnnotation>();
        annotation.Setup(a => a.Value).Returns(name);
        prop.Setup(p => p.FindAnnotation("Relational:ColumnName")).Returns(annotation.Object);
        // GetColumnName() reads the column name through the indexer (property["Relational:ColumnName"]),
        // which Moq will not delegate to FindAnnotation unless set up explicitly — without this the
        // call falls back to GetDefaultColumnName() and throws.  Mirror the other helpers below.
        prop.Setup(p => p["Relational:ColumnName"]).Returns(name);
        entryMock.Setup(e => e.GetCurrentValue(prop.Object)).Returns(value);
        return prop;
    }

    // -----------------------------------------------------------------------
    // Test entities
    // -----------------------------------------------------------------------

    private class EntityWithSetter
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private class EntityWithFieldBackedProperty
    {
        public int Id { get; set; }
#pragma warning disable CS0649
        // ReSharper disable once InconsistentNaming
        private string? _name;
#pragma warning restore CS0649
        public string? Name => _name;
    }

    private class EntityWithComputedProperty
    {
        public int Id { get; set; }
        // Computed — no setter, no backing field.
        public string Name => "computed";
    }

    // -----------------------------------------------------------------------
    // Standard property (PropertyInfo has setter)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_StandardProperty_StoresValueInDocument()
    {
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithSetter));

        var nameProp = MakeProperty(
            typeof(EntityWithSetter).GetProperty(nameof(EntityWithSetter.Name)),
            fieldInfo: null,
            value: "hello",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([nameProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        // HydrateObjectFromEntity now always returns Dictionary<string,object?> so that
        // value converters (HasConversion) can be applied before storage.
        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.True(result.ContainsKey("Name"));
        Assert.Equal("hello", result["Name"]);
    }

    [Fact]
    public void Hydrate_StandardProperty_NullValue_StoresNullInDocument()
    {
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithSetter));

        var nameProp = MakeProperty(
            typeof(EntityWithSetter).GetProperty(nameof(EntityWithSetter.Name)),
            fieldInfo: null,
            value: null,
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([nameProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.True(result.ContainsKey("Name"));
        Assert.Null(result["Name"]);
    }

    // -----------------------------------------------------------------------
    // Field-backed property (no setter on PropertyInfo, FieldInfo present)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_FieldBackedProperty_StoresValueInDocument()
    {
        // Field-backed properties are still accessible by EF Core via GetCurrentValue(),
        // which reads from the change-tracker entry regardless of backing-field strategy.
        // The stored value should appear in the document dictionary.
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithFieldBackedProperty));

        var backingField = typeof(EntityWithFieldBackedProperty)
            .GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(backingField); // guard: ensure the field exists

        var nameProp = MakeProperty(
            typeof(EntityWithFieldBackedProperty).GetProperty(nameof(EntityWithFieldBackedProperty.Name)),
            backingField,
            value: "field-set",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([nameProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.True(result.ContainsKey("Name"));
        Assert.Equal("field-set", result["Name"]);
    }

    // -----------------------------------------------------------------------
    // Shadow property (PropertyInfo == null) — must be skipped
    // IsShadowProperty() returns true when PropertyInfo is null.
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_ShadowProperty_IsSkipped_NoException()
    {
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithSetter));

        // Shadow property: both PropertyInfo and FieldInfo are null →
        // IsShadowProperty() returns true → the property must be omitted from the document.
        var shadowProp = MakeProperty(
            propertyInfo: null,
            fieldInfo: null,
            value: "ignored",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([shadowProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        // Must not throw and the shadow value must not appear in the document.
        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------
    // Computed property (no setter, no FieldInfo) — its EF-tracked value is still
    // written to the document via GetCurrentValue(), regardless of CLR setters.
    // (The old CLR-mutation path would NullReferenceException on PropertyInfo.SetValue.)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_PropertyWithNoSetterNoField_StillStoresValueInDocument()
    {
        // With the dictionary path, GetCurrentValue() supplies the value from the
        // change-tracker regardless of whether the CLR property has a setter.  The
        // value is stored in the document dictionary so it round-trips to Couchbase.
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithComputedProperty));

        // PropertyInfo exists (so IsShadowProperty() == false) but has no setter.
        var computedProp = MakeProperty(
            typeof(EntityWithComputedProperty).GetProperty(nameof(EntityWithComputedProperty.Name)),
            fieldInfo: null,
            value: "ef-tracked-value",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([computedProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        // The dictionary path stores all non-shadow properties regardless of CLR setters.
        Assert.True(result.ContainsKey("Name"));
        Assert.Equal("ef-tracked-value", result["Name"]);
    }

    // -----------------------------------------------------------------------
    // Mixed: shadow + settable in the same entity
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // OwnsOne null navigation — owned scalar columns must be written as null
    // -----------------------------------------------------------------------

    private class OwnedAddress
    {
        public string? Street { get; set; }
        public string? City { get; set; }
    }

    private static INavigation BuildOwnsOneNav(object? navValue)
    {
        var streetProp = BuildOwnedProperty("street", typeof(OwnedAddress).GetProperty(nameof(OwnedAddress.Street))!, navValue);
        var cityProp  = BuildOwnedProperty("city",   typeof(OwnedAddress).GetProperty(nameof(OwnedAddress.City))!,   navValue);

        var targetType = new Mock<IEntityType>();
        targetType.Setup(t => t.GetProperties()).Returns([streetProp, cityProp]);

        var nav = new Mock<INavigation>();
        nav.Setup(n => n.IsCollection).Returns(false);
        nav.Setup(n => n.TargetEntityType).Returns(targetType.Object);

        return nav.Object;
    }

    private static IProperty BuildOwnedProperty(string columnName, PropertyInfo propInfo, object? navValue)
    {
        var prop = new Mock<IProperty>();
        prop.Setup(p => p.PropertyInfo).Returns(propInfo);
        // Wire GetColumnName() via annotation
        var annotation = new Mock<IAnnotation>();
        annotation.Setup(a => a.Value).Returns(columnName);
        prop.Setup(p => p.FindAnnotation("Relational:ColumnName")).Returns(annotation.Object);
        prop.Setup(p => p["Relational:ColumnName"]).Returns(columnName);
        return prop.Object;
    }

    [Fact]
    public void FillOwnsOneIntoDoc_NullNavigation_WritesNullForEachOwnedColumn()
    {
        var doc = new Dictionary<string, object?>();
        var nav = BuildOwnsOneNav(null);

        CouchbaseDatabaseWrapper.FillOwnsOneIntoDoc(doc, nav, navValue: null);

        Assert.True(doc.ContainsKey("street"), "street key must be present");
        Assert.True(doc.ContainsKey("city"),   "city key must be present");
        Assert.Null(doc["street"]);
        Assert.Null(doc["city"]);
    }

    [Fact]
    public void FillOwnsOneIntoDoc_NonNullNavigation_WritesPropertyValues()
    {
        var address = new OwnedAddress { Street = "1 Main St", City = "Springfield" };
        var doc = new Dictionary<string, object?>();
        var nav = BuildOwnsOneNav(address);

        CouchbaseDatabaseWrapper.FillOwnsOneIntoDoc(doc, nav, navValue: address);

        Assert.Equal("1 Main St",  doc["street"]);
        Assert.Equal("Springfield", doc["city"]);
    }

    [Fact]
    public void Hydrate_MixedProperties_StoresNonShadowOmitsShadow()
    {
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithSetter));

        var nameProp = MakeProperty(
            typeof(EntityWithSetter).GetProperty(nameof(EntityWithSetter.Name)),
            fieldInfo: null,
            value: "real",
            entry);

        var shadowProp = MakeProperty(
            propertyInfo: null,
            fieldInfo: null,
            value: "shadow",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([nameProp.Object, shadowProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.True(result.ContainsKey("Name"));
        Assert.Equal("real", result["Name"]);
        // Shadow property (PropertyInfo == null && FieldInfo == null) must be omitted.
        Assert.Single(result);
    }

    // -----------------------------------------------------------------------
    // Shared entity type (HasMany/WithMany hidden join table)
    // -----------------------------------------------------------------------

    private static Mock<IProperty> MakeSharedProperty(
        string columnName,
        object? value,
        Mock<IUpdateEntry> entryMock)
    {
        var prop = new Mock<IProperty>();
        prop.Setup(p => p.PropertyInfo).Returns((PropertyInfo?)null);
        prop.Setup(p => p.FieldInfo).Returns((FieldInfo?)null);
        prop.Setup(p => p.IsShadowProperty()).Returns(true);
        // GetColumnName() is an EF Core extension method — Moq cannot mock it directly.
        // Wire the column name via the relational annotation it reads internally,
        // matching the pattern used by BuildOwnedProperty above.
        var annotation = new Mock<IAnnotation>();
        annotation.Setup(a => a.Value).Returns(columnName);
        prop.Setup(p => p.FindAnnotation("Relational:ColumnName")).Returns(annotation.Object);
        prop.Setup(p => p["Relational:ColumnName"]).Returns(columnName);
        entryMock.Setup(e => e.GetCurrentValue(prop.Object)).Returns(value);
        return prop;
    }

    private static Mock<IEntityType> MakeSharedEntityType(params Mock<IProperty>[] props)
    {
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.HasSharedClrType).Returns(true);
        entityType.Setup(t => t.ClrType).Returns(typeof(Dictionary<string, object>));
        entityType.Setup(t => t.GetProperties()).Returns(props.Select(p => p.Object).ToArray());
        entityType.Setup(t => t.GetNavigations()).Returns([]);
        return entityType;
    }

    [Fact]
    public void Hydrate_SharedEntityType_UseColumnNameVerbatim()
    {
        // The join document keys must be GetColumnName() verbatim — not policy-transformed.
        // If camelCase were applied, "PostsPostId" → "postsPostId" which would diverge
        // from the SQL projection alias and make the document unreadable on the query path.
        var entry = new Mock<IUpdateEntry>();
        var postsPostId = MakeSharedProperty("PostsPostId", 1,    entry);
        var tagsTagId   = MakeSharedProperty("TagsTagId",   "abc", entry);
        var entityType  = MakeSharedEntityType(postsPostId, tagsTagId);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        // Pass camelCase policy — must NOT be applied to column names.
        var result = (Dictionary<string, object?>)Hydrate(entry.Object, JsonNamingPolicy.CamelCase);

        Assert.True(result.ContainsKey("PostsPostId"),
            "Expected verbatim column name 'PostsPostId' — naming policy must not be applied.");
        Assert.True(result.ContainsKey("TagsTagId"),
            "Expected verbatim column name 'TagsTagId' — naming policy must not be applied.");
        Assert.Equal(1,     result["PostsPostId"]);
        Assert.Equal("abc", result["TagsTagId"]);
    }

    [Fact]
    public void Hydrate_SharedEntityType_DoesNotContainPolicyTransformedKeys()
    {
        // Negative assertion: camelCased variants of the column names must NOT appear.
        var entry = new Mock<IUpdateEntry>();
        var postsPostId = MakeSharedProperty("PostsPostId", 1,    entry);
        var tagsTagId   = MakeSharedProperty("TagsTagId",   "abc", entry);
        var entityType  = MakeSharedEntityType(postsPostId, tagsTagId);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object, JsonNamingPolicy.CamelCase);

        Assert.False(result.ContainsKey("postsPostId"),
            "camelCase-transformed key 'postsPostId' must not appear — naming policy must not be applied.");
        Assert.False(result.ContainsKey("tagsTagId"),
            "camelCase-transformed key 'tagsTagId' must not appear — naming policy must not be applied.");
    }

    [Fact]
    public void Hydrate_SharedEntityType_NullPolicyAlso_UsesColumnNameVerbatim()
    {
        // Sanity check: null policy also uses GetColumnName() verbatim.
        var entry = new Mock<IUpdateEntry>();
        var prop = MakeSharedProperty("PostsPostId", 42, entry);
        var entityType = MakeSharedEntityType(prop);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object, policy: null);

        Assert.True(result.ContainsKey("PostsPostId"));
        Assert.Equal(42, result["PostsPostId"]);
    }

    [Fact]
    public void Hydrate_SharedEntityType_IncludesAllProperties()
    {
        // All properties (shadow or not) must appear in the join document.
        var entry = new Mock<IUpdateEntry>();
        var p1 = MakeSharedProperty("PostsPostId", 5,       entry);
        var p2 = MakeSharedProperty("TagsTagId",   "tag1",  entry);
        var entityType = MakeSharedEntityType(p1, p2);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.Equal(2, result.Count);
    }

    // -----------------------------------------------------------------------
    // Value converters (HasConversion) — regular entity write path
    //
    // Uses a real DbContext + real IEntityType / IProperty so that EF Core's
    // type-mapping pipeline populates the converter metadata correctly.
    // -----------------------------------------------------------------------

    private enum ConverterStatusEnum { Active, Inactive }

    private class RegularEntityWithConverter
    {
        public int Id { get; set; }
        public ConverterStatusEnum Status { get; set; }
        public string? Name { get; set; }
    }

    private class RegularConverterContext(DbContextOptions<RegularConverterContext> options)
        : DbContext(options)
    {
        public DbSet<RegularEntityWithConverter> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RegularEntityWithConverter>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entities");
                b.HasKey(e => e.Id);
                b.Property(e => e.Status).HasConversion<string>();
            });
        }
    }

    private static RegularConverterContext CreateRegularConverterContext()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<RegularConverterContext>();
        builder.UseCouchbaseProvider(opts);
        return new RegularConverterContext(builder.Options);
    }

    [Fact]
    public void Hydrate_PropertyWithConverter_StoresProviderValue()
    {
        // HasConversion<string> on a ConverterStatusEnum property: the stored document
        // must contain the provider type ("Active") rather than the model CLR value
        // (ConverterStatusEnum.Active / integer 0).
        using var ctx = CreateRegularConverterContext();
        var entityType = ctx.Model.FindEntityType(typeof(RegularEntityWithConverter))!;
        var idProp     = entityType.FindProperty(nameof(RegularEntityWithConverter.Id))!;
        var statusProp = entityType.FindProperty(nameof(RegularEntityWithConverter.Status))!;
        var nameProp   = entityType.FindProperty(nameof(RegularEntityWithConverter.Name))!;

        var entry = new Mock<IUpdateEntry>();
        entry.Setup(e => e.EntityType).Returns(entityType);
        entry.Setup(e => e.GetCurrentValue(idProp)).Returns(1);
        entry.Setup(e => e.GetCurrentValue(statusProp)).Returns(ConverterStatusEnum.Active);
        entry.Setup(e => e.GetCurrentValue(nameProp)).Returns("Alice");

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        // Converter must produce the provider string, not the raw enum.
        Assert.Equal("Active", result[statusProp.GetColumnName()]);
        Assert.Equal("Alice",  result[nameProp.GetColumnName()]);
        Assert.Equal(1,        result[idProp.GetColumnName()]);
    }

    [Fact]
    public void Hydrate_PropertyWithConverter_InactiveVariant()
    {
        using var ctx = CreateRegularConverterContext();
        var entityType = ctx.Model.FindEntityType(typeof(RegularEntityWithConverter))!;
        var statusProp = entityType.FindProperty(nameof(RegularEntityWithConverter.Status))!;
        var idProp     = entityType.FindProperty(nameof(RegularEntityWithConverter.Id))!;
        var nameProp   = entityType.FindProperty(nameof(RegularEntityWithConverter.Name))!;

        var entry = new Mock<IUpdateEntry>();
        entry.Setup(e => e.EntityType).Returns(entityType);
        entry.Setup(e => e.GetCurrentValue(statusProp)).Returns(ConverterStatusEnum.Inactive);
        entry.Setup(e => e.GetCurrentValue(idProp)).Returns(2);
        entry.Setup(e => e.GetCurrentValue(nameProp)).Returns((object?)null);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.Equal("Inactive", result[statusProp.GetColumnName()]);
    }

    [Fact]
    public void Hydrate_PropertyWithConverter_NullValue_NoConverter_StoredAsNull()
    {
        // A nullable property WITHOUT a converter: null must be stored verbatim.
        using var ctx = CreateRegularConverterContext();
        var entityType = ctx.Model.FindEntityType(typeof(RegularEntityWithConverter))!;
        var nameProp   = entityType.FindProperty(nameof(RegularEntityWithConverter.Name))!;
        var idProp     = entityType.FindProperty(nameof(RegularEntityWithConverter.Id))!;
        var statusProp = entityType.FindProperty(nameof(RegularEntityWithConverter.Status))!;

        var entry = new Mock<IUpdateEntry>();
        entry.Setup(e => e.EntityType).Returns(entityType);
        entry.Setup(e => e.GetCurrentValue(nameProp)).Returns((object?)null);
        entry.Setup(e => e.GetCurrentValue(idProp)).Returns(1);
        entry.Setup(e => e.GetCurrentValue(statusProp)).Returns(ConverterStatusEnum.Active);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.Null(result[nameProp.GetColumnName()]);
    }

    // -----------------------------------------------------------------------
    // ConvertsNulls=true — null model value must reach the converter
    // -----------------------------------------------------------------------

    private sealed class NullSentinelConverter
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string?, string>
    {
        public NullSentinelConverter()
            : base(v => v ?? "NULL_SENTINEL", v => v == "NULL_SENTINEL" ? null : v) { }

        public override bool ConvertsNulls => true;
    }

    private class NullSentinelEntity
    {
        public int     Id   { get; set; }
        public string? Note { get; set; }
    }

    private class NullSentinelEntityContext(DbContextOptions<NullSentinelEntityContext> options)
        : DbContext(options)
    {
        public DbSet<NullSentinelEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullSentinelEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "entities");
                b.HasKey(e => e.Id);
                b.Property(e => e.Note).HasConversion(new NullSentinelConverter());
            });
        }
    }

    private static NullSentinelEntityContext CreateNullSentinelEntityContext()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<NullSentinelEntityContext>();
        builder.UseCouchbaseProvider(opts);
        return new NullSentinelEntityContext(builder.Options);
    }

    [Fact]
    public void Hydrate_ConvertsNulls_True_NullModelValue_StoresSentinel()
    {
        // A converter with ConvertsNulls=true must be invoked even when the model value
        // is null, so that null → "NULL_SENTINEL" is written to Couchbase.
        using var ctx = CreateNullSentinelEntityContext();
        var entityType = ctx.Model.FindEntityType(typeof(NullSentinelEntity))!;
        var noteProp   = entityType.FindProperty(nameof(NullSentinelEntity.Note))!;
        var idProp     = entityType.FindProperty(nameof(NullSentinelEntity.Id))!;

        var entry = new Mock<IUpdateEntry>();
        entry.Setup(e => e.EntityType).Returns(entityType);
        entry.Setup(e => e.GetCurrentValue(noteProp)).Returns((object?)null);
        entry.Setup(e => e.GetCurrentValue(idProp)).Returns(1);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        Assert.Equal("NULL_SENTINEL", result[noteProp.GetColumnName()]);
    }

    [Fact]
    public void Hydrate_ConvertsNulls_True_NonNullModelValue_StoresConverted()
    {
        using var ctx = CreateNullSentinelEntityContext();
        var entityType = ctx.Model.FindEntityType(typeof(NullSentinelEntity))!;
        var noteProp   = entityType.FindProperty(nameof(NullSentinelEntity.Note))!;
        var idProp     = entityType.FindProperty(nameof(NullSentinelEntity.Id))!;

        var entry = new Mock<IUpdateEntry>();
        entry.Setup(e => e.EntityType).Returns(entityType);
        entry.Setup(e => e.GetCurrentValue(noteProp)).Returns("hello");
        entry.Setup(e => e.GetCurrentValue(idProp)).Returns(1);

        var result = (Dictionary<string, object?>)Hydrate(entry.Object);

        // Non-null value passes through unchanged (v => v ?? "NULL_SENTINEL" keeps "hello").
        Assert.Equal("hello", result[noteProp.GetColumnName()]);
    }

    // -----------------------------------------------------------------------
    // FillOwnsOneIntoDoc — value converter on an OwnsOne scalar
    // -----------------------------------------------------------------------

    private class OwnedDetails
    {
        public ConverterStatusEnum Status { get; set; }
        public string?              Label  { get; set; }
    }

    private class OwnerWithOwnsOne
    {
        public int         Id      { get; set; }
        public OwnedDetails Details { get; set; } = null!;
    }

    private class OwnsOneConverterContext(DbContextOptions<OwnsOneConverterContext> options)
        : DbContext(options)
    {
        public DbSet<OwnerWithOwnsOne> Owners { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OwnerWithOwnsOne>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "owners");
                b.HasKey(o => o.Id);
                b.OwnsOne(o => o.Details, d =>
                {
                    d.Property(x => x.Status).HasConversion<string>();
                });
            });
        }
    }

    private static OwnsOneConverterContext CreateOwnsOneConverterContext()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<OwnsOneConverterContext>();
        builder.UseCouchbaseProvider(opts);
        return new OwnsOneConverterContext(builder.Options);
    }

    [Fact]
    public void FillOwnsOneIntoDoc_PropertyWithConverter_StoresProviderValue()
    {
        // HasConversion<string> on an OwnsOne scalar: FillOwnsOneIntoDoc must apply
        // ConvertToProvider so the document stores "Active" not the enum integer.
        using var ctx = CreateOwnsOneConverterContext();
        var ownerEntityType = ctx.Model.FindEntityType(typeof(OwnerWithOwnsOne))!;
        var detailsNav      = ownerEntityType.GetNavigations()
            .First(n => n.Name == nameof(OwnerWithOwnsOne.Details));

        var navValue = new OwnedDetails { Status = ConverterStatusEnum.Active, Label = "primary" };
        var doc      = new Dictionary<string, object?>();

        CouchbaseDatabaseWrapper.FillOwnsOneIntoDoc(doc, detailsNav, navValue);

        // Status must be stored as the provider string, not the enum / integer.
        var statusKey = detailsNav.TargetEntityType
            .FindProperty(nameof(OwnedDetails.Status))!.GetColumnName();
        Assert.Equal("Active", doc[statusKey]);

        var labelKey = detailsNav.TargetEntityType
            .FindProperty(nameof(OwnedDetails.Label))!.GetColumnName();
        Assert.Equal("primary", doc[labelKey]);
    }

    [Fact]
    public void FillOwnsOneIntoDoc_NullNavigation_WithConverter_WritesNullForEachColumn()
    {
        // When the entire OwnsOne navigation is null every column must be written as null
        // regardless of whether a converter is present.
        using var ctx = CreateOwnsOneConverterContext();
        var ownerEntityType = ctx.Model.FindEntityType(typeof(OwnerWithOwnsOne))!;
        var detailsNav      = ownerEntityType.GetNavigations()
            .First(n => n.Name == nameof(OwnerWithOwnsOne.Details));

        var doc = new Dictionary<string, object?>();
        CouchbaseDatabaseWrapper.FillOwnsOneIntoDoc(doc, detailsNav, navValue: null);

        var statusKey = detailsNav.TargetEntityType
            .FindProperty(nameof(OwnedDetails.Status))!.GetColumnName();
        Assert.Null(doc[statusKey]);
    }

    // FillOwnsOneIntoDoc — ConvertsNulls=true converter on an OwnsOne scalar
    // -----------------------------------------------------------------------
    // Regression: when navValue is null, ApplyConverter must NOT be called even
    // when the property's converter has ConvertsNulls=true.  Calling it would write
    // a non-null sentinel and EF would materialise a phantom owned object on read.

    private class OwnedDetailsWithSentinel
    {
        public string? Note { get; set; }
    }

    private class OwnerWithSentinelOwnsOne
    {
        public int                    Id      { get; set; }
        public OwnedDetailsWithSentinel? Details { get; set; }
    }

    private class OwnsOneWithSentinelConverterContext(DbContextOptions<OwnsOneWithSentinelConverterContext> options)
        : DbContext(options)
    {
        public DbSet<OwnerWithSentinelOwnsOne> Owners { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OwnerWithSentinelOwnsOne>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "owners");
                b.HasKey(o => o.Id);
                b.OwnsOne(o => o.Details, d =>
                {
                    // NullSentinelConverter has ConvertsNulls=true and maps null → "NULL_SENTINEL".
                    // When the navigation itself is null FillOwnsOneIntoDoc must still write null,
                    // not the sentinel, for every owned column.
                    d.Property(x => x.Note).HasConversion(new NullSentinelConverter());
                });
            });
        }
    }

    private static OwnsOneWithSentinelConverterContext CreateOwnsOneWithSentinelConverterContext()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<OwnsOneWithSentinelConverterContext>();
        builder.UseCouchbaseProvider(opts);
        return new OwnsOneWithSentinelConverterContext(builder.Options);
    }

    [Fact]
    public void FillOwnsOneIntoDoc_NullNavigation_ConvertsNullsConverter_WritesNullNotSentinel()
    {
        // Regression: a ConvertsNulls=true converter on an OwnsOne property must NOT be
        // invoked when the navigation itself is null.  Before the fix, ApplyConverter was
        // called with rawValue=null and ConvertsNulls=true, causing "NULL_SENTINEL" to be
        // written — EF would then materialise a phantom owned object on the read path.
        using var ctx = CreateOwnsOneWithSentinelConverterContext();
        var ownerEntityType = ctx.Model.FindEntityType(typeof(OwnerWithSentinelOwnsOne))!;
        var detailsNav      = ownerEntityType.GetNavigations()
            .First(n => n.Name == nameof(OwnerWithSentinelOwnsOne.Details));

        var doc = new Dictionary<string, object?>();
        CouchbaseDatabaseWrapper.FillOwnsOneIntoDoc(doc, detailsNav, navValue: null);

        var noteKey = detailsNav.TargetEntityType
            .FindProperty(nameof(OwnedDetailsWithSentinel.Note))!.GetColumnName();

        // Must be null — not "NULL_SENTINEL" — because the navigation is absent.
        Assert.Null(doc[noteKey]);
    }

    // -----------------------------------------------------------------------
    // SerializeOwnedItem — nested owned navigation via field access
    //
    // A nested OwnsOne/OwnsMany whose PropertyInfo is null (field-access, e.g.
    // a backing field or [BackingField] navigation) must still be serialized by
    // reading through FieldInfo, not silently dropped.
    // -----------------------------------------------------------------------

    private class OwnedLabel
    {
        public string? Text { get; set; }
    }

    private class OwnedContact
    {
        // Nested owned navigation accessed via this backing field (no CLR property
        // exposed to EF), so the mocked INavigation.PropertyInfo is null.
        private OwnedLabel? _label;

        public OwnedLabel? GetLabel() => _label;
        public void SetLabel(OwnedLabel? label) => _label = label;
    }

    private static INavigation BuildNestedOwnsOneNavViaField(string propName)
    {
        // SerializeOwnedItem keys owned scalars by p.Name, so wire Name (not just the
        // column annotation) and a PropertyInfo to read the value through.
        var textProp = new Mock<IProperty>();
        textProp.Setup(p => p.PropertyInfo).Returns(typeof(OwnedLabel).GetProperty(nameof(OwnedLabel.Text)));
        textProp.Setup(p => p.FieldInfo).Returns((FieldInfo?)null);
        textProp.Setup(p => p.IsShadowProperty()).Returns(false);
        textProp.Setup(p => p.Name).Returns(propName);

        var targetType = new Mock<IEntityType>();
        targetType.Setup(t => t.GetProperties()).Returns([textProp.Object]);
        targetType.Setup(t => t.GetNavigations()).Returns([]);
        targetType.Setup(t => t.IsOwned()).Returns(true);

        var nav = new Mock<INavigation>();
        nav.Setup(n => n.IsCollection).Returns(false);
        nav.Setup(n => n.Name).Returns("label");
        nav.Setup(n => n.TargetEntityType).Returns(targetType.Object);
        // PropertyInfo is null (field-access); the FieldInfo points at the backing field.
        nav.Setup(n => n.PropertyInfo).Returns((PropertyInfo?)null);
        nav.Setup(n => n.FieldInfo).Returns(
            typeof(OwnedContact).GetField("_label", BindingFlags.NonPublic | BindingFlags.Instance));

        return nav.Object;
    }

    [Fact]
    public void SerializeOwnedItem_NestedNavViaFieldAccess_IsSerialized()
    {
        // Regression: before the FieldInfo fallback, a nested owned navigation with
        // PropertyInfo == null was skipped entirely, dropping its data from the document.
        var contact = new OwnedContact();
        contact.SetLabel(new OwnedLabel { Text = "primary" });

        var nestedNav = BuildNestedOwnsOneNavViaField("text");

        var contactType = new Mock<IEntityType>();
        contactType.Setup(t => t.GetProperties()).Returns([]);
        contactType.Setup(t => t.GetNavigations()).Returns([nestedNav]);

        var result = CouchbaseDatabaseWrapper.SerializeOwnedItem(
            contact, contactType.Object, fieldNamingPolicy: null);

        // The nested navigation must be present and read via FieldInfo.
        Assert.True(result.ContainsKey("label"), "nested nav 'label' must be serialized");
        var nested = Assert.IsType<Dictionary<string, object?>>(result["label"]);
        Assert.Equal("primary", nested["text"]);
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
