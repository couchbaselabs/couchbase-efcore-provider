using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseBoolTypeMappingTests
{
    [Fact]
    public void Constructor_SetsStoreTypeToBoolean()
    {
        // Act
        var mapping = new CouchbaseBoolTypeMapping();

        // Assert
        Assert.Equal("BOOLEAN", mapping.StoreType);
    }

    [Fact]
    public void Constructor_SetsClrTypeToBool()
    {
        // Act
        var mapping = new CouchbaseBoolTypeMapping();

        // Assert
        Assert.Equal(typeof(bool), mapping.ClrType);
    }

    [Fact]
    public void GenerateSqlLiteral_WithTrue_ReturnsTRUE()
    {
        // Arrange
        var mapping = new CouchbaseBoolTypeMapping();

        // Act
        var literal = mapping.GenerateSqlLiteral(true);

        // Assert
        Assert.Equal("TRUE", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithFalse_ReturnsFALSE()
    {
        // Arrange
        var mapping = new CouchbaseBoolTypeMapping();

        // Act
        var literal = mapping.GenerateSqlLiteral(false);

        // Assert
        Assert.Equal("FALSE", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithNull_ReturnsNULL()
    {
        // Arrange
        var mapping = new CouchbaseBoolTypeMapping();

        // Act
        var literal = mapping.GenerateSqlLiteral(null);

        // Assert
        Assert.Equal("NULL", literal);
    }

    [Fact]
    public void Clone_PreservesStoreType()
    {
        // Arrange
        var original = new CouchbaseBoolTypeMapping();

        // Act - Clone is called internally through WithComposedConverter
        var cloned = original.WithComposedConverter(null);

        // Assert
        Assert.Equal("BOOLEAN", ((RelationalTypeMapping)cloned).StoreType);
    }

    [Fact]
    public void Clone_ReturnsCouchbaseBoolTypeMapping()
    {
        // Arrange
        var original = new CouchbaseBoolTypeMapping();

        // Act
        var cloned = original.WithComposedConverter(null);

        // Assert
        Assert.IsType<CouchbaseBoolTypeMapping>(cloned);
    }

    [Fact]
    public void ClonedMapping_GenerateSqlLiteral_StillReturnsTrueFalse()
    {
        // Arrange
        var original = new CouchbaseBoolTypeMapping();
        var cloned = (CouchbaseBoolTypeMapping)original.WithComposedConverter(null);

        // Act
        var trueLiteral = cloned.GenerateSqlLiteral(true);
        var falseLiteral = cloned.GenerateSqlLiteral(false);

        // Assert
        Assert.Equal("TRUE", trueLiteral);
        Assert.Equal("FALSE", falseLiteral);
    }

    [Fact]
    public void Mapping_InheritsBoolTypeMapping()
    {
        // Act
        var mapping = new CouchbaseBoolTypeMapping();

        // Assert
        Assert.IsAssignableFrom<BoolTypeMapping>(mapping);
    }
}
