using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseSequenceValueGeneratorTests
{
    private readonly ISqlGenerationHelper _sqlGenerationHelper = new CouchbaseSqlGenerationHelper(
        new RelationalSqlGenerationHelperDependencies());

    [Fact]
    public void Constructor_WithValidArguments_Succeeds()
    {
        // Arrange & Act
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope");

        // Assert
        Assert.NotNull(generator);
    }

    [Fact]
    public void Constructor_WithNullSequenceName_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator<long>(
            null!,
            "bucket",
            "scope"));
    }

    [Fact]
    public void Constructor_WithEmptySequenceName_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator<long>(
            "",
            "bucket",
            "scope"));
    }

    [Fact]
    public void Constructor_WithNullBucket_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            null!,
            "scope"));
    }

    [Fact]
    public void Constructor_WithEmptyBucket_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "",
            "scope"));
    }

    [Fact]
    public void Constructor_WithNullScope_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            null!));
    }

    [Fact]
    public void Constructor_WithEmptyScope_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            ""));
    }

    [Fact]
    public void BuildSequenceQuery_ReturnsCorrectSqlPlusPlus()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "order_seq",
            "myBucket",
            "myScope");

        // Act
        var query = generator.BuildSequenceQuery(_sqlGenerationHelper);

        // Assert - Each part should be separately escaped with backticks
        Assert.Equal("SELECT NEXT VALUE FOR `myBucket`.`myScope`.`order_seq`", query);
    }

    [Fact]
    public void GeneratesTemporaryValues_ReturnsFalse()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope");

        // Act & Assert
        Assert.False(generator.GeneratesTemporaryValues);
    }

    [Fact]
    public void BuildSequenceQuery_WithSpecialCharactersInNames_EscapesCorrectly()
    {
        // Arrange - Names that would need escaping
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "order-seq",
            "my-bucket",
            "my-scope");

        // Act
        var query = generator.BuildSequenceQuery(_sqlGenerationHelper);

        // Assert - Backticks protect special characters
        Assert.Equal("SELECT NEXT VALUE FOR `my-bucket`.`my-scope`.`order-seq`", query);
    }

    [Fact]
    public void BuildSequenceQuery_UsesSqlGenerationHelper()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope");

        // Act
        var query = generator.BuildSequenceQuery(_sqlGenerationHelper);

        // Assert - Verify the query uses the helper's delimiter format (backticks for Couchbase)
        Assert.StartsWith("SELECT NEXT VALUE FOR `", query);
        Assert.Contains("`.`", query); // Helper delimits each part
        Assert.EndsWith("`", query);
    }

    #region Type Support Tests

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(short))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(decimal))]
    public void IsTypeSupported_ForSupportedTypes_ReturnsTrue(Type type)
    {
        // Act & Assert
        Assert.True(CouchbaseSequenceValueGenerator<int>.IsTypeSupported(type));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(bool))]
    public void IsTypeSupported_ForUnsupportedTypes_ReturnsFalse(Type type)
    {
        // Act & Assert
        Assert.False(CouchbaseSequenceValueGenerator<int>.IsTypeSupported(type));
    }

    [Fact]
    public void Constructor_WithUnsupportedType_ThrowsInvalidOperationException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => 
            new CouchbaseSequenceValueGenerator<Guid>(
                "test_seq",
                "bucket",
                "scope"));

        Assert.Contains("Guid", ex.Message);
        Assert.Contains("not supported", ex.Message);
    }

    #endregion
}
