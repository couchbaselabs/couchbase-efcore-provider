using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;
using JsonNamingPolicy = System.Text.Json.JsonNamingPolicy;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Tests for PopulateCollectionNavigations to verify it uses the serializer's
/// PropertyNamingPolicy applied to the CLR property name when reading OwnsMany item
/// properties — not GetColumnName(), which returns the EF Core relational column name.
/// </summary>
public class PopulateCollectionNavigationsTests
{
    // CLR types used by the tests
    private class OwnerEntity
    {
        public List<OwnedItem> Items { get; set; } = [];
    }

    private class OwnedItem
    {
        public string? Value { get; set; }
    }

    // Direct call — PopulateCollectionNavigations now lives in CouchbaseOwnedCollectionMaterializer.
    private static readonly CouchbaseOwnedCollectionMaterializer _materializer = new();

    private static void Populate(OwnerEntity entity, JsonElement doc, IReadOnlyList<INavigation> navs,
        JsonNamingPolicy? policy = null, JsonSerializerOptions? serializerOptions = null)
        => _materializer.Populate(entity, doc, navs, policy, serializerOptions);

    /// <summary>
    /// Builds a mock INavigation whose single owned property has CLR name "Value"
    /// but is remapped to column name <paramref name="columnName"/> via HasColumnName.
    /// </summary>
    private static INavigation BuildNav(string columnName)
    {
        var propInfo = typeof(OwnedItem).GetProperty(nameof(OwnedItem.Value))!;

        var prop = new Mock<IProperty>();
        prop.Setup(p => p.Name).Returns("Value");
        prop.Setup(p => p.ClrType).Returns(typeof(string));
        prop.Setup(p => p.PropertyInfo).Returns(propInfo);
        // Wire up the annotation so GetColumnName() resolves to columnName.
        // GetColumnName() reads property["Relational:ColumnName"] which delegates to FindAnnotation.
        var annotation = new Mock<IAnnotation>();
        annotation.Setup(a => a.Value).Returns(columnName);
        prop.Setup(p => p.FindAnnotation("Relational:ColumnName")).Returns(annotation.Object);
        prop.Setup(p => p["Relational:ColumnName"]).Returns(columnName);

        var targetType = new Mock<IEntityType>();
        targetType.Setup(t => t.ClrType).Returns(typeof(OwnedItem));
        targetType.Setup(t => t.GetProperties()).Returns([prop.Object]);

        var navPropInfo = typeof(OwnerEntity).GetProperty(nameof(OwnerEntity.Items))!;
        var nav = new Mock<INavigation>();
        nav.Setup(n => n.Name).Returns("items");
        nav.Setup(n => n.PropertyInfo).Returns(navPropInfo);
        nav.Setup(n => n.TargetEntityType).Returns(targetType.Object);
        nav.Setup(n => n.IsCollection).Returns(true);

        return nav.Object;
    }

    [Fact]
    public void PopulateCollectionNavigations_reads_property_using_clr_name_and_naming_policy()
    {
        // The SDK serializes owned items using the CLR property name run through the naming policy
        // (camelCase by default). CLR name "Value" → JSON key "value".
        // The EF Core column name ("col_irrelevant") is not used for the lookup.
        var nav = BuildNav("col_irrelevant");
        var doc = JsonDocument.Parse("""{"items": [{"value": "hello"}]}""").RootElement;
        var entity = new OwnerEntity();

        Populate(entity, doc, [nav], serializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(entity.Items);
        Assert.Single(entity.Items);
        Assert.Equal("hello", entity.Items[0].Value);
    }

    [Fact]
    public void PopulateCollectionNavigations_ignores_HasColumnName_remapping()
    {
        // HasColumnName("foo") remaps the EF Core column but does not affect the JSON key —
        // the SDK writes the field under the CLR property name (camelCase: "value"), not "foo".
        // Previously GetColumnName() returned "foo" here, causing a silent null for the property.
        var nav = BuildNav("foo");
        var doc = JsonDocument.Parse("""{"items": [{"value": "hello"}]}""").RootElement;
        var entity = new OwnerEntity();

        Populate(entity, doc, [nav], serializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(entity.Items);
        Assert.Single(entity.Items);
        Assert.Equal("hello", entity.Items[0].Value);
    }

    [Fact]
    public void PopulateCollectionNavigations_does_not_find_value_keyed_by_column_name()
    {
        // Inverse of the above: if JSON is somehow keyed on the EF column name "foo" rather
        // than the CLR name, the lookup misses — confirming GetColumnName() is not used.
        var nav = BuildNav("foo");
        var doc = JsonDocument.Parse("""{"items": [{"foo": "hello"}]}""").RootElement;
        var entity = new OwnerEntity();

        Populate(entity, doc, [nav], serializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(entity.Items);
        Assert.Single(entity.Items);
        Assert.Null(entity.Items[0].Value);
    }

    [Fact]
    public void PopulateCollectionNavigations_uses_naming_policy_for_multi_uppercase_nav_name()
    {
        // nav.Name = "URLs"; JsonNamingPolicy.CamelCase converts this to "urls", not "uRLs".
        // Without the policy, TryGetPropertyCI would search for "URLs" and still find "urls"
        // via case-insensitive match — but only because OrdinalIgnoreCase treats them the same.
        // This test pins that the policy is applied so the lookup key is "urls" (correct),
        // not the result of a manual char.ToLowerInvariant which would produce "uRLs".
        var propInfo = typeof(OwnedItem).GetProperty(nameof(OwnedItem.Value))!;
        var prop = new Mock<IProperty>();
        prop.Setup(p => p.Name).Returns("Value");
        prop.Setup(p => p.ClrType).Returns(typeof(string));
        prop.Setup(p => p.PropertyInfo).Returns(propInfo);
        var annotation = new Mock<IAnnotation>();
        annotation.Setup(a => a.Value).Returns("Value");
        prop.Setup(p => p.FindAnnotation("Relational:ColumnName")).Returns(annotation.Object);
        prop.Setup(p => p["Relational:ColumnName"]).Returns("Value");

        var targetType = new Mock<IEntityType>();
        targetType.Setup(t => t.ClrType).Returns(typeof(OwnedItem));
        targetType.Setup(t => t.GetProperties()).Returns([prop.Object]);

        var navPropInfo = typeof(OwnerEntity).GetProperty(nameof(OwnerEntity.Items))!;
        var nav = new Mock<INavigation>();
        nav.Setup(n => n.Name).Returns("URLs"); // multi-uppercase CLR name
        nav.Setup(n => n.PropertyInfo).Returns(navPropInfo);
        nav.Setup(n => n.TargetEntityType).Returns(targetType.Object);
        nav.Setup(n => n.IsCollection).Returns(true);

        // JsonNamingPolicy.CamelCase converts "URLs" → "urls"
        var doc = JsonDocument.Parse("""{"urls": [{"Value": "https://example.com"}]}""").RootElement;
        var entity = new OwnerEntity();

        Populate(entity, doc, [nav.Object], JsonNamingPolicy.CamelCase);

        Assert.NotNull(entity.Items);
        Assert.Single(entity.Items);
        Assert.Equal("https://example.com", entity.Items[0].Value);
    }
}
