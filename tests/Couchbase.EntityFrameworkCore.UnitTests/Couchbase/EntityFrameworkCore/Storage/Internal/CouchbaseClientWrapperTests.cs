using Couchbase.Core.Exceptions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using ManagementCollectionNotFoundException = Couchbase.Management.Collections.CollectionNotFoundException;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapperTests
{
    private readonly Mock<IBucketProvider> _mockBucketProvider;
    private readonly Mock<ICouchbaseDbContextOptionsBuilder> _mockOptions;
    private readonly Mock<ILogger<CouchbaseClientWrapper>> _mockLogger;
    private readonly Mock<IBucket> _mockBucket;
    private readonly Mock<IScope> _mockScope;

    public CouchbaseClientWrapperTests()
    {
        _mockBucketProvider = new Mock<IBucketProvider>();
        _mockOptions = new Mock<ICouchbaseDbContextOptionsBuilder>();
        _mockLogger = new Mock<ILogger<CouchbaseClientWrapper>>();
        _mockBucket = new Mock<IBucket>();
        _mockScope = new Mock<IScope>();

        _mockOptions.Setup(o => o.Bucket).Returns("test-bucket");
        _mockBucketProvider.Setup(p => p.GetBucketAsync("test-bucket"))
            .ReturnsAsync(_mockBucket.Object);
    }

    [Fact]
    public async Task GetCollectionAsync_WithInvalidKeyspace_ThrowsExceptionWithCorrectFormat()
    {
        // Arrange: Standard format is Bucket.Scope.Collection
        var keyspace = "MyBucket.MyScope.MyCollection";

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection"))
            .Throws(new ManagementCollectionNotFoundException("Collection not found"));

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CollectionNotFoundException>(
            () => wrapper.GetCollectionAsync(keyspace));

        // The exception message should contain the display format with backticks: `Bucket`.`Scope`.`Collection`
        Assert.Contains("`MyBucket`.`MyScope`.`MyCollection`", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_WithBackticksInKeyspace_FormatsDisplayKeyspaceCorrectly()
    {
        // Arrange: Standard format with backticks
        var keyspace = "`MyBucket`.`MyScope`.`MyCollection`";

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection"))
            .Throws(new ManagementCollectionNotFoundException("Collection not found"));

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CollectionNotFoundException>(
            () => wrapper.GetCollectionAsync(keyspace));

        // The exception message should have proper backtick format (no double backticks)
        Assert.Contains("`MyBucket`.`MyScope`.`MyCollection`", exception.Message);
        Assert.DoesNotContain("``", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_WithValidCollection_ReturnsCollection()
    {
        // Arrange: Standard format is Bucket.Scope.Collection
        var keyspace = "MyBucket.MyScope.MyCollection";
        var mockCollection = new Mock<ICouchbaseCollection>();

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection")).Returns(mockCollection.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act
        var collection = await wrapper.GetCollectionAsync(keyspace);

        // Assert
        Assert.Same(mockCollection.Object, collection);
    }

    [Fact]
    public async Task GetCollectionAsync_CalledTwice_UsesCachedCollection()
    {
        // Arrange: Standard format is Bucket.Scope.Collection
        var keyspace = "MyBucket.MyScope.MyCollection";
        var mockCollection = new Mock<ICouchbaseCollection>();

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection")).Returns(mockCollection.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act
        var collection1 = await wrapper.GetCollectionAsync(keyspace);
        var collection2 = await wrapper.GetCollectionAsync(keyspace);

        // Assert - Collection should only be retrieved once from scope
        Assert.Same(collection1, collection2);
        _mockScope.Verify(s => s.Collection("MyCollection"), Times.Once);
    }

    [Fact]
    public async Task GetCollectionAsync_WithMalformedKeyspace_ThrowsExceptionWithOriginalKeyspace()
    {
        // Arrange: Malformed keyspace (not 3 parts)
        var malformedKeyspace = "InvalidKeyspace";

        _mockBucketProvider.Setup(p => p.GetBucketAsync("test-bucket"))
            .ThrowsAsync(new Exception("Bucket error"));

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CollectionNotFoundException>(
            () => wrapper.GetCollectionAsync(malformedKeyspace));

        // For malformed keyspace, it should return the original string
        Assert.Contains("InvalidKeyspace", exception.Message);
    }
}
