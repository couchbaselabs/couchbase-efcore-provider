using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseByteArrayTypeMappingTests
{
    [Fact]
    public void Constructor_SetsStoreTypeToString()
    {
        // Act
        var mapping = new CouchbaseByteArrayTypeMapping();

        // Assert
        Assert.Equal("STRING", mapping.StoreType);
    }

    [Fact]
    public void Constructor_SetsClrTypeToByteArray()
    {
        // Act
        var mapping = new CouchbaseByteArrayTypeMapping();

        // Assert
        Assert.Equal(typeof(byte[]), mapping.ClrType);
    }

    [Fact]
    public void GenerateSqlLiteral_WithByteArray_ReturnsBase64String()
    {
        // Arrange
        var mapping = new CouchbaseByteArrayTypeMapping();
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello" in ASCII

        // Act
        var literal = mapping.GenerateSqlLiteral(bytes);

        // Assert - Should be Base64 encoded in single quotes
        Assert.Equal("'SGVsbG8='", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithEmptyByteArray_ReturnsEmptyBase64()
    {
        // Arrange
        var mapping = new CouchbaseByteArrayTypeMapping();
        var bytes = Array.Empty<byte>();

        // Act
        var literal = mapping.GenerateSqlLiteral(bytes);

        // Assert
        Assert.Equal("''", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithNull_ReturnsNULL()
    {
        // Arrange
        var mapping = new CouchbaseByteArrayTypeMapping();

        // Act
        var literal = mapping.GenerateSqlLiteral(null);

        // Assert
        Assert.Equal("NULL", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithBinaryData_ProducesValidBase64()
    {
        // Arrange
        var mapping = new CouchbaseByteArrayTypeMapping();
        var bytes = new byte[] { 0x00, 0xFF, 0x7F, 0x80, 0xAB, 0xCD };

        // Act
        var literal = mapping.GenerateSqlLiteral(bytes);

        // Assert - Verify it's a valid Base64 string in single quotes
        Assert.StartsWith("'", literal);
        Assert.EndsWith("'", literal);
        
        // Extract and decode to verify round-trip
        var base64 = literal.Trim('\'');
        var decoded = Convert.FromBase64String(base64);
        Assert.Equal(bytes, decoded);
    }

    [Fact]
    public void GenerateSqlLiteral_IsValidSqlPlusPlus_UsesSingleQuotes()
    {
        // Arrange
        var mapping = new CouchbaseByteArrayTypeMapping();
        var bytes = new byte[] { 1, 2, 3 };

        // Act
        var literal = mapping.GenerateSqlLiteral(bytes);

        // Assert - SQL++ string literals use single quotes, not hex format
        Assert.StartsWith("'", literal);
        Assert.EndsWith("'", literal);
        Assert.DoesNotContain("0x", literal); // Not hex format
    }

    [Fact]
    public void Clone_PreservesStoreType()
    {
        // Arrange
        var original = new CouchbaseByteArrayTypeMapping();

        // Act - Clone is called internally through WithComposedConverter
        var cloned = original.WithComposedConverter(null);

        // Assert
        Assert.Equal("STRING", ((RelationalTypeMapping)cloned).StoreType);
    }

    [Fact]
    public void Clone_ReturnsCouchbaseByteArrayTypeMapping()
    {
        // Arrange
        var original = new CouchbaseByteArrayTypeMapping();

        // Act
        var cloned = original.WithComposedConverter(null);

        // Assert
        Assert.IsType<CouchbaseByteArrayTypeMapping>(cloned);
    }

    [Fact]
    public void Mapping_InheritsRelationalTypeMapping()
    {
        // Act
        var mapping = new CouchbaseByteArrayTypeMapping();

        // Assert
        Assert.IsAssignableFrom<RelationalTypeMapping>(mapping);
    }

    [Fact]
    public void GenerateSqlLiteral_WithLargeByteArray_ProducesValidBase64()
    {
        // Arrange
        var mapping = new CouchbaseByteArrayTypeMapping();
        var bytes = new byte[1000];
        new Random(42).NextBytes(bytes);

        // Act
        var literal = mapping.GenerateSqlLiteral(bytes);

        // Assert
        Assert.StartsWith("'", literal);
        Assert.EndsWith("'", literal);
        
        // Verify round-trip
        var base64 = literal.Trim('\'');
        var decoded = Convert.FromBase64String(base64);
        Assert.Equal(bytes, decoded);
    }
}
