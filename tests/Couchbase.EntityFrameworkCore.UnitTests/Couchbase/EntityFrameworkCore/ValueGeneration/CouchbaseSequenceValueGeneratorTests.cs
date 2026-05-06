using Couchbase.EntityFrameworkCore.ValueGeneration;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseSequenceValueGeneratorTests
{
    [Fact]
    public void Constructor_WithValidArguments_Succeeds()
    {
        // Arrange & Act
        var generator = new CouchbaseSequenceValueGenerator(
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
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator(
            null!,
            "bucket",
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithEmptySequenceName_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator(
            "",
            "bucket",
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithNullBucket_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator(
            "test_seq",
            null!,
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithEmptyBucket_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator(
            "test_seq",
            "",
            "scope",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithNullScope_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator(
            "test_seq",
            "bucket",
            null!,
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithEmptyScope_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceValueGenerator(
            "test_seq",
            "bucket",
            "",
            _ => Task.FromResult(1L)));
    }

    [Fact]
    public void Constructor_WithNullQueryExecutor_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceValueGenerator(
            "test_seq",
            "bucket",
            "scope",
            null!));
    }

    [Fact]
    public void SequenceQuery_ReturnsCorrectSqlPlusPlus()
    {
        // Arrange
        var generator = new CouchbaseSequenceValueGenerator(
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
        var generator = new CouchbaseSequenceValueGenerator(
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
        var generator = new CouchbaseSequenceValueGenerator(
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
        var generator = new CouchbaseSequenceValueGenerator(
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
        var generator = new CouchbaseSequenceValueGenerator(
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
        var generator = new CouchbaseSequenceValueGenerator(
            "order-seq",
            "my-bucket",
            "my-scope",
            _ => Task.FromResult(1L));

        // Act
        var query = generator.SequenceQuery;

        // Assert - Backticks protect special characters
        Assert.Equal("SELECT NEXT VALUE FOR `my-bucket`.`my-scope`.`order-seq`", query);
    }
}
