using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.KeyValue;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Tests for the TransactionOperation record and related types.
/// Note: CouchbaseDbTransaction requires real CouchbaseConnection which cannot be mocked,
/// so we test the TransactionOperation behavior directly.
/// </summary>
public class TransactionOperationTests
{
    [Fact]
    public void TransactionOperation_Insert_HasCorrectProperties()
    {
        // Arrange
        var mockCollection = new Mock<ICouchbaseCollection>();
        var content = new { Name = "Test" };

        // Act
        var operation = new TransactionOperation(
            TransactionOperationType.Insert,
            mockCollection.Object,
            "test-id",
            content);

        // Assert
        Assert.Equal(TransactionOperationType.Insert, operation.OperationType);
        Assert.Equal("test-id", operation.Id);
        Assert.Same(content, operation.Content);
        Assert.Same(mockCollection.Object, operation.Collection);
    }

    [Fact]
    public void TransactionOperation_Upsert_HasCorrectProperties()
    {
        // Arrange
        var mockCollection = new Mock<ICouchbaseCollection>();
        var content = new { Name = "Test" };

        // Act
        var operation = new TransactionOperation(
            TransactionOperationType.Upsert,
            mockCollection.Object,
            "test-id",
            content);

        // Assert
        Assert.Equal(TransactionOperationType.Upsert, operation.OperationType);
        Assert.Equal("test-id", operation.Id);
        Assert.Same(content, operation.Content);
    }

    [Fact]
    public void TransactionOperation_Remove_HasNullContent()
    {
        // Arrange
        var mockCollection = new Mock<ICouchbaseCollection>();

        // Act
        var operation = new TransactionOperation(
            TransactionOperationType.Remove,
            mockCollection.Object,
            "test-id",
            null);

        // Assert
        Assert.Equal(TransactionOperationType.Remove, operation.OperationType);
        Assert.Equal("test-id", operation.Id);
        Assert.Null(operation.Content);
    }

    [Fact]
    public void TransactionOperationType_HasExpectedValues()
    {
        // Assert that all expected operation types exist
        Assert.Equal(0, (int)TransactionOperationType.Insert);
        Assert.Equal(1, (int)TransactionOperationType.Upsert);
        Assert.Equal(2, (int)TransactionOperationType.Remove);
    }
}
