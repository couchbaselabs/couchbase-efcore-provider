using System.Collections.Generic;
using System.Reflection;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Tests for CouchbaseDatabaseWrapper.HydrateObjectFromEntity via reflection.
///
/// The method is private static, so we invoke it directly rather than going through
/// the full SaveChangesAsync stack (which requires live EF Core model infrastructure
/// for GetPrimaryKey, GetCollectionName, IsOwned, etc.).
/// </summary>
public class CouchbaseDatabaseWrapperHydrateTests
{
    private static readonly MethodInfo HydrateMethod =
        typeof(CouchbaseDatabaseWrapper)
            .GetMethod("HydrateObjectFromEntity", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static object Hydrate(IUpdateEntry entry)
        => HydrateMethod.Invoke(null, [entry, null])!;

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
    public void Hydrate_StandardProperty_SetsValueViaPropertyInfo()
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

        var result = (EntityWithSetter)Hydrate(entry.Object);

        Assert.Equal("hello", result.Name);
    }

    [Fact]
    public void Hydrate_StandardProperty_NullValue_SetsNull()
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

        var result = (EntityWithSetter)Hydrate(entry.Object);

        Assert.Null(result.Name);
    }

    // -----------------------------------------------------------------------
    // Field-backed property (no setter on PropertyInfo, FieldInfo present)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_FieldBackedProperty_SetsValueViaFieldInfo()
    {
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

        var result = (EntityWithFieldBackedProperty)Hydrate(entry.Object);

        Assert.Equal("field-set", result.Name);
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

        // Shadow property: both PropertyInfo and FieldInfo are null.
        var shadowProp = MakeProperty(
            propertyInfo: null,
            fieldInfo: null,
            value: "ignored",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([shadowProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        // Must not throw and the Name property must remain at its default.
        var result = (EntityWithSetter)Hydrate(entry.Object);

        Assert.Null(result.Name);
    }

    // -----------------------------------------------------------------------
    // Computed property (no setter, no FieldInfo) — must be skipped silently.
    // Before the fix this would NullReferenceException on propertyInfo.SetValue.
    // -----------------------------------------------------------------------

    [Fact]
    public void Hydrate_NoSetterNoField_IsSkipped_NoNullReferenceException()
    {
        var entry = new Mock<IUpdateEntry>();
        var entityType = new Mock<IEntityType>();
        entityType.Setup(t => t.ClrType).Returns(typeof(EntityWithComputedProperty));

        // PropertyInfo exists (so IsShadowProperty() == false) but has no setter.
        // FieldInfo is null.
        var computedProp = MakeProperty(
            typeof(EntityWithComputedProperty).GetProperty(nameof(EntityWithComputedProperty.Name)),
            fieldInfo: null,
            value: "should-be-ignored",
            entry);

        entityType.Setup(t => t.GetProperties()).Returns([computedProp.Object]);
        entry.Setup(e => e.EntityType).Returns(entityType.Object);

        var ex = Record.Exception(() => Hydrate(entry.Object));

        Assert.Null(ex);
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
    public void Hydrate_MixedProperties_SetsSettableSkipsShadow()
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

        var result = (EntityWithSetter)Hydrate(entry.Object);

        Assert.Equal("real", result.Name);
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
