using Couchbase.EntityFrameworkCore.ValueGeneration;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseGuidStringValueGeneratorTests
{
    [Fact]
    public void Constructor_DefaultFormat_UsesD()
    {
        // Act
        var generator = new CouchbaseGuidStringValueGenerator();

        // Assert
        Assert.Equal("D", generator.Format);
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    public void Constructor_ValidFormat_SetsFormat(string format)
    {
        // Act
        var generator = new CouchbaseGuidStringValueGenerator(format);

        // Assert
        Assert.Equal(format, generator.Format);
    }

    [Theory]
    [InlineData("X")]
    [InlineData("d")]
    [InlineData("")]
    [InlineData("invalid")]
    public void Constructor_InvalidFormat_ThrowsArgumentException(string format)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseGuidStringValueGenerator(format));
    }

    [Fact]
    public void GeneratesTemporaryValues_ReturnsFalse()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator();

        // Act & Assert
        Assert.False(generator.GeneratesTemporaryValues);
    }

    [Fact]
    public void Next_FormatD_GeneratesHyphenatedGuid()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator("D");

        // Act
        var value = generator.Next(null!);

        // Assert
        Assert.NotNull(value);
        Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", value);
        Assert.Equal(36, value.Length);
    }

    [Fact]
    public void Next_FormatN_GeneratesNoHyphensGuid()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator("N");

        // Act
        var value = generator.Next(null!);

        // Assert
        Assert.NotNull(value);
        Assert.Matches(@"^[0-9a-f]{32}$", value);
        Assert.Equal(32, value.Length);
    }

    [Fact]
    public void Next_FormatB_GeneratesBracedGuid()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator("B");

        // Act
        var value = generator.Next(null!);

        // Assert
        Assert.NotNull(value);
        Assert.StartsWith("{", value);
        Assert.EndsWith("}", value);
        Assert.Equal(38, value.Length);
    }

    [Fact]
    public void Next_FormatP_GeneratesParenthesizedGuid()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator("P");

        // Act
        var value = generator.Next(null!);

        // Assert
        Assert.NotNull(value);
        Assert.StartsWith("(", value);
        Assert.EndsWith(")", value);
        Assert.Equal(38, value.Length);
    }

    [Fact]
    public void Next_CalledMultipleTimes_GeneratesUniqueValues()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator();
        var values = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            values.Add(generator.Next(null!));
        }

        // Assert - all values should be unique
        Assert.Equal(100, values.Count);
    }

    [Fact]
    public void Next_GeneratedValue_CanBeParsedAsGuid()
    {
        // Arrange
        var generator = new CouchbaseGuidStringValueGenerator("D");

        // Act
        var value = generator.Next(null!);

        // Assert
        Assert.True(Guid.TryParse(value, out var parsed));
        Assert.NotEqual(Guid.Empty, parsed);
    }
}
