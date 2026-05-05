using System.Text.Json.Nodes;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseTypeMappingTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithClrTypeAndStoreType_SetsProperties()
    {
        // Act
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Assert
        Assert.Equal(typeof(JsonObject), mapping.ClrType);
        Assert.Equal("OBJECT", mapping.StoreType);
    }

    [Fact]
    public void Constructor_WithArrayStoreType_SetsStoreType()
    {
        // Act
        var mapping = new CouchbaseTypeMapping(typeof(JsonArray), "ARRAY");

        // Assert
        Assert.Equal(typeof(JsonArray), mapping.ClrType);
        Assert.Equal("ARRAY", mapping.StoreType);
    }

    [Theory]
    [InlineData(typeof(JsonObject), "OBJECT")]
    [InlineData(typeof(JsonArray), "ARRAY")]
    [InlineData(typeof(object), "CUSTOM")]
    public void Constructor_WithVariousTypes_SetsCorrectStoreType(Type clrType, string storeType)
    {
        // Act
        var mapping = new CouchbaseTypeMapping(clrType, storeType);

        // Assert
        Assert.Equal(clrType, mapping.ClrType);
        Assert.Equal(storeType, mapping.StoreType);
    }

    #endregion

    #region Clone Tests

    [Fact]
    public void Clone_PreservesClrType()
    {
        // Arrange
        var original = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Act - Clone is called internally through WithComposedConverter
        var cloned = original.WithComposedConverter(null);

        // Assert
        Assert.Equal(typeof(JsonObject), cloned.ClrType);
        Assert.IsType<CouchbaseTypeMapping>(cloned);
    }

    [Fact]
    public void Clone_PreservesStoreType()
    {
        // Arrange
        var original = new CouchbaseTypeMapping(typeof(JsonArray), "ARRAY");

        // Act
        var cloned = original.WithComposedConverter(null);

        // Assert
        Assert.Equal("ARRAY", ((CouchbaseTypeMapping)cloned).StoreType);
    }

    #endregion

    #region WithComposedConverter Tests

    [Fact]
    public void WithComposedConverter_WithNullConverter_ReturnsCouchbaseTypeMapping()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Act
        var result = mapping.WithComposedConverter(null);

        // Assert
        Assert.IsType<CouchbaseTypeMapping>(result);
    }

    [Fact]
    public void WithComposedConverter_WithConverter_ReturnsNewInstance()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");
        var converter = new ValueConverter<JsonObject, string>(
            v => v.ToJsonString(),
            v => JsonNode.Parse(v)!.AsObject());

        // Act
        var result = mapping.WithComposedConverter(converter);

        // Assert
        Assert.IsType<CouchbaseTypeMapping>(result);
        Assert.NotSame(mapping, result);
    }

    [Fact]
    public void WithComposedConverter_PreservesStoreType()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Act
        var result = mapping.WithComposedConverter(null);

        // Assert
        Assert.Equal("OBJECT", ((CouchbaseTypeMapping)result).StoreType);
    }

    #endregion

    #region SqlLiteralFormatString Tests

    [Fact]
    public void GenerateSqlLiteral_FormatsWithDoubleQuotes()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(string), "STRING");

        // Act
        var literal = mapping.GenerateSqlLiteral("test");

        // Assert
        Assert.Equal("\"test\"", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithDiscriminatorValue_FormatsCorrectly()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(string), "STRING");

        // Act
        var literal = mapping.GenerateSqlLiteral("MyEntityType");

        // Assert
        Assert.Equal("\"MyEntityType\"", literal);
    }

    #endregion

    #region Type Preservation Tests

    [Fact]
    public void Mapping_IsRelationalTypeMapping()
    {
        // Arrange & Act
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Assert
        Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping>(mapping);
    }

    [Fact]
    public void MultipleClones_AllReturnCouchbaseTypeMapping()
    {
        // Arrange
        var original = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Act
        var clone1 = original.WithComposedConverter(null);
        var clone2 = clone1.WithComposedConverter(null);
        var clone3 = clone2.WithComposedConverter(null);

        // Assert
        Assert.IsType<CouchbaseTypeMapping>(clone1);
        Assert.IsType<CouchbaseTypeMapping>(clone2);
        Assert.IsType<CouchbaseTypeMapping>(clone3);
    }

    #endregion
}
