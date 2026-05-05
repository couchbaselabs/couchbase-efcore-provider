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

    #region GenerateNonNullSqlLiteral Tests

    [Fact]
    public void GenerateSqlLiteral_WithJsonObject_EmitsRawJson()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");
        var jsonObject = new JsonObject
        {
            ["name"] = "test",
            ["value"] = 42
        };

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonObject);

        // Assert - Should be raw JSON, not quoted
        Assert.Equal("{\"name\":\"test\",\"value\":42}", literal);
        Assert.DoesNotContain("\"{\\'", literal); // Not double-escaped
    }

    [Fact]
    public void GenerateSqlLiteral_WithJsonArray_EmitsRawJson()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonArray), "ARRAY");
        var jsonArray = new JsonArray { 1, 2, 3 };

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonArray);

        // Assert - Should be raw JSON array, not quoted
        Assert.Equal("[1,2,3]", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithEmptyJsonObject_EmitsEmptyObject()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");
        var jsonObject = new JsonObject();

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonObject);

        // Assert
        Assert.Equal("{}", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithEmptyJsonArray_EmitsEmptyArray()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonArray), "ARRAY");
        var jsonArray = new JsonArray();

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonArray);

        // Assert
        Assert.Equal("[]", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithNestedJsonObject_EmitsValidJson()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");
        var jsonObject = new JsonObject
        {
            ["outer"] = new JsonObject
            {
                ["inner"] = "value"
            }
        };

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonObject);

        // Assert
        Assert.Equal("{\"outer\":{\"inner\":\"value\"}}", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithJsonObjectContainingArray_EmitsValidJson()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");
        var jsonObject = new JsonObject
        {
            ["items"] = new JsonArray { "a", "b", "c" }
        };

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonObject);

        // Assert
        Assert.Equal("{\"items\":[\"a\",\"b\",\"c\"]}", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithNull_ReturnsNull()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");

        // Act
        var literal = mapping.GenerateSqlLiteral(null);

        // Assert
        Assert.Equal("NULL", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithJsonObjectContainingSpecialChars_EmitsProperlyEscapedJson()
    {
        // Arrange
        var mapping = new CouchbaseTypeMapping(typeof(JsonObject), "OBJECT");
        var jsonObject = new JsonObject
        {
            ["text"] = "line1\nline2\ttab",
            ["quote"] = "say \"hello\""
        };

        // Act
        var literal = mapping.GenerateSqlLiteral(jsonObject);

        // Assert - JSON escaping should be handled by System.Text.Json
        // Newline and tab are escaped
        Assert.Contains(@"\n", literal);
        Assert.Contains(@"\t", literal);
        // System.Text.Json escapes quotes as \u0022
        Assert.Contains(@"\u0022", literal);
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
