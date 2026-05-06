using Couchbase.EntityFrameworkCore.ValueGeneration;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseSequenceValueGeneratorTests
{
    [Fact]
    public void Constructor_WithValidArguments_Succeeds()
    {
        // Arrange & Act
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult(1L));

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
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithEmptySequenceName_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator<long>(
            "",
            "bucket",
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithNullBucket_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            null!,
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithEmptyBucket_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "",
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithNullScope_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            null!,
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithEmptyScope_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithNullQueryExecutor_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope",
            null!));
    }

    [Fact]
    public void SequenceQuery_ReturnsCorrectSqlPlusPlus()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "order_seq",
            "myBucket",
            "myScope",
            _ => Task.FromResult(1L));

        // Act
        var query = generator.SequenceQuery;

        // Assert - Each part should be separately escaped
        Assert.Equal("SELECT NEXT VALUE FOR `myBucket`.`myScope`.`order_seq`", query);
    }

    [Fact]
    public void GeneratesTemporaryValues_ReturnsFalse()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult(1L));

        // Act & Assert
        Assert.False(generator.GeneratesTemporaryValues);
    }

    [Fact]
    public async Task NextAsync_CallsQueryExecutor()
    {
        // Arrange
        var queryExecuted = false;
        var expectedValue = 42L;
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope",
            query =>
            {
                queryExecuted = true;
                return Task.FromResult(expectedValue);
            });

        // Act
        var result = await generator.NextAsync(null!);

        // Assert
        Assert.True(queryExecuted);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public async Task NextAsync_PassesCorrectQuery()
    {
        // Arrange
        string? capturedQuery = null;
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "my_sequence",
            "testBucket",
            "testScope",
            query =>
            {
                capturedQuery = query;
                return Task.FromResult(1L);
            });

        // Act
        await generator.NextAsync(null!);

        // Assert
        Assert.Equal("SELECT NEXT VALUE FOR `testBucket`.`testScope`.`my_sequence`", capturedQuery);
    }

    [Fact]
    public async Task NextAsync_ReturnsIncrementingValues()
    {
        // Arrange
        var counter = 0L;
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult(++counter));

        // Act
        var value1 = await generator.NextAsync(null!);
        var value2 = await generator.NextAsync(null!);
        var value3 = await generator.NextAsync(null!);

        // Assert
        Assert.Equal(1L, value1);
        Assert.Equal(2L, value2);
        Assert.Equal(3L, value3);
    }

    [Fact]
    public void SequenceQuery_WithSpecialCharactersInNames_EscapesCorrectly()
    {
        // Arrange - Names that would need escaping
        var generator = new CouchbaseSequenceValueGenerator<long>(
            "order-seq",
            "my-bucket",
            "my-scope",
            _ => Task.FromResult(1L));

        // Act
        var query = generator.SequenceQuery;

        // Assert - Backticks protect special characters
        Assert.Equal("SELECT NEXT VALUE FOR `my-bucket`.`my-scope`.`order-seq`", query);
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
                "scope",
                _ => Task.FromResult(1L)));

        Assert.Contains("Guid", ex.Message);
        Assert.Contains("not supported", ex.Message);
    }

    [Fact]
    public async Task IntGenerator_ReturnsCorrectType()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<int>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult(42L));

        // Act
        var result = await generator.NextAsync(null!);

        // Assert
        Assert.IsType<int>(result);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ShortGenerator_ReturnsCorrectType()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<short>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult(100L));

        // Act
        var result = await generator.NextAsync(null!);

        // Assert
        Assert.IsType<short>(result);
        Assert.Equal((short)100, result);
    }

    [Fact]
    public async Task DecimalGenerator_ReturnsCorrectType()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator<decimal>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult(999L));

        // Act
        var result = await generator.NextAsync(null!);

        // Assert
        Assert.IsType<decimal>(result);
        Assert.Equal(999m, result);
    }

    [Fact]
    public async Task IntGenerator_WithOverflow_ThrowsOverflowException()
    {
        // Arrange - Value larger than int.MaxValue
        var generator = new CouchbaseSequenceValueGenerator<int>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult((long)int.MaxValue + 1));

        // Act & Assert
        await Assert.ThrowsAsync<OverflowException>(() => 
            generator.NextAsync(null!).AsTask());
    }

    [Fact]
    public async Task ShortGenerator_WithOverflow_ThrowsOverflowException()
    {
        // Arrange - Value larger than short.MaxValue
        var generator = new CouchbaseSequenceValueGenerator<short>(
            "test_seq",
            "bucket",
            "scope",
            _ => Task.FromResult((long)short.MaxValue + 1));

        // Act & Assert
        await Assert.ThrowsAsync<OverflowException>(() => 
            generator.NextAsync(null!).AsTask());
    }

    #endregion
}
