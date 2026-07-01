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
        // Arrange: Standard format is Bucket.Scope.Collection - use configured bucket
        var keyspace = "test-bucket.MyScope.MyCollection";

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
        Assert.Contains("`test-bucket`.`MyScope`.`MyCollection`", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_WithBackticksInKeyspace_FormatsDisplayKeyspaceCorrectly()
    {
        // Arrange: Standard format with backticks - use configured bucket
        var keyspace = "`test-bucket`.`MyScope`.`MyCollection`";

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
        Assert.Contains("`test-bucket`.`MyScope`.`MyCollection`", exception.Message);
        Assert.DoesNotContain("``", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_WithValidCollection_ReturnsCollection()
    {
        // Arrange: Standard format is Bucket.Scope.Collection - use configured bucket
        var keyspace = "test-bucket.MyScope.MyCollection";
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
    public async Task GetCollectionAsync_WithDifferentBucket_OpensThatBucket()
    {
        // A single context may span multiple buckets on the same cluster: a keyspace naming a
        // bucket other than the configured one opens that bucket rather than being rejected.
        var keyspace = "other-bucket.MyScope.MyCollection";
        var mockOtherBucket = new Mock<IBucket>();
        var mockOtherScope = new Mock<IScope>();
        var mockCollection = new Mock<ICouchbaseCollection>();

        _mockBucketProvider.Setup(p => p.GetBucketAsync("other-bucket"))
            .ReturnsAsync(mockOtherBucket.Object);
        mockOtherBucket.Setup(b => b.Scope("MyScope")).Returns(mockOtherScope.Object);
        mockOtherScope.Setup(s => s.Collection("MyCollection")).Returns(mockCollection.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act
        var collection = await wrapper.GetCollectionAsync(keyspace);

        // Assert - resolves the collection from the keyspace's own bucket.
        Assert.Same(mockCollection.Object, collection);
        _mockBucketProvider.Verify(p => p.GetBucketAsync("other-bucket"), Times.Once);
    }

    [Fact]
    public async Task GetCollectionAsync_UsesKeyspaceBucketNameVerbatim()
    {
        // Bucket names are case-sensitive in Couchbase; the keyspace's bucket segment is used
        // exactly as written, not coerced to the context's configured bucket.
        var keyspace = "Exact-Bucket.MyScope.MyCollection";
        var mockExactBucket = new Mock<IBucket>();
        var mockExactScope = new Mock<IScope>();
        var mockCollection = new Mock<ICouchbaseCollection>();

        _mockBucketProvider.Setup(p => p.GetBucketAsync("Exact-Bucket"))
            .ReturnsAsync(mockExactBucket.Object);
        mockExactBucket.Setup(b => b.Scope("MyScope")).Returns(mockExactScope.Object);
        mockExactScope.Setup(s => s.Collection("MyCollection")).Returns(mockCollection.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act
        var collection = await wrapper.GetCollectionAsync(keyspace);

        // Assert - opened the exact bucket named in the keyspace, not "test-bucket".
        Assert.Same(mockCollection.Object, collection);
        _mockBucketProvider.Verify(p => p.GetBucketAsync("Exact-Bucket"), Times.Once);
        _mockBucketProvider.Verify(p => p.GetBucketAsync("test-bucket"), Times.Never);
    }

    [Fact]
    public async Task GetCollectionAsync_CalledTwice_UsesCachedCollection()
    {
        // Arrange: Standard format is Bucket.Scope.Collection - use configured bucket
        var keyspace = "test-bucket.MyScope.MyCollection";
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
    public async Task GetCollectionAsync_WithMalformedKeyspace_ThrowsArgumentException()
    {
        // Arrange: Malformed keyspace (not 3 parts)
        var malformedKeyspace = "InvalidKeyspace";

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert - Malformed keyspace throws ArgumentException
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.GetCollectionAsync(malformedKeyspace));

        Assert.Contains("Invalid keyspace format", exception.Message);
        Assert.Contains("InvalidKeyspace", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_WithEmptyParts_ThrowsArgumentException()
    {
        // Arrange: Keyspace with empty scope
        var keyspaceWithEmptyScope = "test-bucket..collection";

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert - Empty parts should be rejected
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.GetCollectionAsync(keyspaceWithEmptyScope));

        Assert.Contains("Invalid keyspace format", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_WithEmptyCollection_ThrowsArgumentException()
    {
        // Arrange: Keyspace with empty collection
        var keyspaceWithEmptyCollection = "test-bucket.scope.";

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert - Empty collection should be rejected
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.GetCollectionAsync(keyspaceWithEmptyCollection));

        Assert.Contains("Invalid keyspace format", exception.Message);
    }

    [Fact]
    public async Task GetCollectionAsync_DifferentKeyspaces_CachesEachSeparately()
    {
        // Arrange: Two different keyspaces
        var keyspace1 = "test-bucket.Scope1.Collection1";
        var keyspace2 = "test-bucket.Scope2.Collection2";
        var mockCollection1 = new Mock<ICouchbaseCollection>();
        var mockCollection2 = new Mock<ICouchbaseCollection>();
        var mockScope1 = new Mock<IScope>();
        var mockScope2 = new Mock<IScope>();

        _mockBucket.Setup(b => b.Scope("Scope1")).Returns(mockScope1.Object);
        _mockBucket.Setup(b => b.Scope("Scope2")).Returns(mockScope2.Object);
        mockScope1.Setup(s => s.Collection("Collection1")).Returns(mockCollection1.Object);
        mockScope2.Setup(s => s.Collection("Collection2")).Returns(mockCollection2.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act
        var collection1a = await wrapper.GetCollectionAsync(keyspace1);
        var collection2a = await wrapper.GetCollectionAsync(keyspace2);
        var collection1b = await wrapper.GetCollectionAsync(keyspace1);
        var collection2b = await wrapper.GetCollectionAsync(keyspace2);

        // Assert - Each keyspace cached separately
        Assert.Same(mockCollection1.Object, collection1a);
        Assert.Same(mockCollection1.Object, collection1b);
        Assert.Same(mockCollection2.Object, collection2a);
        Assert.Same(mockCollection2.Object, collection2b);
        mockScope1.Verify(s => s.Collection("Collection1"), Times.Once);
        mockScope2.Verify(s => s.Collection("Collection2"), Times.Once);
    }

    [Fact]
    public async Task GetCollectionAsync_ConcurrentCallsSameKeyspace_OnlyResolvesOnce()
    {
        // Arrange
        var keyspace = "test-bucket.MyScope.MyCollection";
        var mockCollection = new Mock<ICouchbaseCollection>();
        var resolutionCount = 0;

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection"))
            .Returns(() =>
            {
                Interlocked.Increment(ref resolutionCount);
                return mockCollection.Object;
            });

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act - Launch multiple concurrent requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => wrapper.GetCollectionAsync(keyspace))
            .ToList();

        var collections = await Task.WhenAll(tasks);

        // Assert - All should return same collection, resolved only once
        Assert.All(collections, c => Assert.Same(mockCollection.Object, c));
        Assert.Equal(1, resolutionCount);
    }

    [Fact]
    public async Task GetCollectionAsync_ConcurrentCallsDifferentKeyspaces_ResolvesEachOnce()
    {
        // Arrange
        var mockScope1 = new Mock<IScope>();
        var mockScope2 = new Mock<IScope>();
        var mockCollection1 = new Mock<ICouchbaseCollection>();
        var mockCollection2 = new Mock<ICouchbaseCollection>();

        _mockBucket.Setup(b => b.Scope("Scope1")).Returns(mockScope1.Object);
        _mockBucket.Setup(b => b.Scope("Scope2")).Returns(mockScope2.Object);
        mockScope1.Setup(s => s.Collection("Collection1")).Returns(mockCollection1.Object);
        mockScope2.Setup(s => s.Collection("Collection2")).Returns(mockCollection2.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act - Launch concurrent requests for two keyspaces
        var tasks = new List<Task<ICouchbaseCollection>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(wrapper.GetCollectionAsync("test-bucket.Scope1.Collection1"));
            tasks.Add(wrapper.GetCollectionAsync("test-bucket.Scope2.Collection2"));
        }

        var collections = await Task.WhenAll(tasks);

        // Assert - Each collection resolved only once
        mockScope1.Verify(s => s.Collection("Collection1"), Times.Once);
        mockScope2.Verify(s => s.Collection("Collection2"), Times.Once);
    }

    [Fact]
    public async Task GetCollectionAsync_AfterFailedResolution_CanRetrySuccessfully()
    {
        // Arrange: First call fails, second succeeds
        var keyspace = "test-bucket.MyScope.MyCollection";
        var mockCollection = new Mock<ICouchbaseCollection>();
        var callCount = 0;

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection"))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new ManagementCollectionNotFoundException("Collection not found");
                }
                return mockCollection.Object;
            });

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act & Assert - First call fails
        await Assert.ThrowsAsync<CollectionNotFoundException>(
            () => wrapper.GetCollectionAsync(keyspace));

        // Second call should succeed (not cached because first failed)
        var collection = await wrapper.GetCollectionAsync(keyspace);
        Assert.Same(mockCollection.Object, collection);
    }

    [Fact]
    public async Task GetCollectionAsync_BucketProviderCalledOncePerKeyspace()
    {
        // Arrange
        var keyspace = "test-bucket.MyScope.MyCollection";
        var mockCollection = new Mock<ICouchbaseCollection>();

        _mockBucket.Setup(b => b.Scope("MyScope")).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.Collection("MyCollection")).Returns(mockCollection.Object);

        var wrapper = new CouchbaseClientWrapper(
            _mockBucketProvider.Object,
            _mockOptions.Object,
            _mockLogger.Object);

        // Act
        await wrapper.GetCollectionAsync(keyspace);
        await wrapper.GetCollectionAsync(keyspace);
        await wrapper.GetCollectionAsync(keyspace);

        // Assert - Bucket provider should be called only once due to caching
        _mockBucketProvider.Verify(p => p.GetBucketAsync("test-bucket"), Times.Once);
    }
}
