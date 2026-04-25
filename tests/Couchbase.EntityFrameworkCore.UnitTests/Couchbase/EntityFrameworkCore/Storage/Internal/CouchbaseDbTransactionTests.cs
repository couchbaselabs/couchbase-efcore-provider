using System.Data;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDbTransactionTests
{
    private readonly Mock<IBucketProvider> _mockBucketProvider;
    private readonly Mock<ICouchbaseDbContextOptionsBuilder> _mockOptions;
    private readonly Mock<IBucket> _mockBucket;
    private readonly Mock<ICluster> _mockCluster;
    private readonly Mock<ICouchbaseCollection> _mockCollection;
    private CouchbaseConnection _connection = null!;

    public CouchbaseDbTransactionTests()
    {
        _mockBucketProvider = new Mock<IBucketProvider>();
        _mockOptions = new Mock<ICouchbaseDbContextOptionsBuilder>();
        _mockBucket = new Mock<IBucket>();
        _mockCluster = new Mock<ICluster>();
        _mockCollection = new Mock<ICouchbaseCollection>();

        _mockOptions.Setup(o => o.Bucket).Returns("test-bucket");
        _mockOptions.Setup(o => o.ConnectionString).Returns("couchbase://localhost?bucket=test-bucket");
        _mockOptions.Setup(o => o.ClusterOptions).Returns(new ClusterOptions().WithConnectionString("couchbase://localhost"));

        _mockBucket.Setup(b => b.Cluster).Returns(_mockCluster.Object);
        _mockBucketProvider.Setup(p => p.GetBucketAsync("test-bucket"))
            .ReturnsAsync(_mockBucket.Object);
    }

    private async Task<CouchbaseDbTransaction> CreateTransactionAsync()
    {
        _connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await _connection.OpenAsync();
        return (CouchbaseDbTransaction)_connection.BeginTransaction();
    }

    [Fact]
    public async Task IsolationLevel_ReturnsExpectedValue()
    {
        var transaction = await CreateTransactionAsync();

        Assert.Equal(IsolationLevel.Unspecified, transaction.IsolationLevel);
    }

    [Fact]
    public async Task Connection_ReturnsParentConnection()
    {
        var transaction = await CreateTransactionAsync();

        Assert.Same(_connection, transaction.Connection);
    }

    [Fact]
    public async Task PendingOperations_InitiallyEmpty()
    {
        var transaction = await CreateTransactionAsync();

        Assert.Empty(transaction.PendingOperations);
    }

    [Fact]
    public async Task EnqueueInsert_AddsToPendingOperations()
    {
        var transaction = await CreateTransactionAsync();
        var entity = new { Id = "1", Name = "Test" };

        transaction.EnqueueInsert(_mockCollection.Object, "doc1", entity);

        Assert.Single(transaction.PendingOperations);
        Assert.Equal("doc1", transaction.PendingOperations[0].Id);
    }

    [Fact]
    public async Task EnqueueUpsert_AddsToPendingOperations()
    {
        var transaction = await CreateTransactionAsync();
        var entity = new { Id = "1", Name = "Test" };

        transaction.EnqueueUpsert(_mockCollection.Object, "doc1", entity);

        Assert.Single(transaction.PendingOperations);
    }

    [Fact]
    public async Task EnqueueRemove_AddsToPendingOperations()
    {
        var transaction = await CreateTransactionAsync();

        transaction.EnqueueRemove(_mockCollection.Object, "doc1");

        Assert.Single(transaction.PendingOperations);
    }

    [Fact]
    public async Task MultipleEnqueue_AccumulatesOperations()
    {
        var transaction = await CreateTransactionAsync();

        transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test1" });
        transaction.EnqueueUpsert(_mockCollection.Object, "doc2", new { Name = "Test2" });
        transaction.EnqueueRemove(_mockCollection.Object, "doc3");

        Assert.Equal(3, transaction.PendingOperations.Count);
    }

    [Fact]
    public async Task Rollback_ClearsPendingOperations()
    {
        var transaction = await CreateTransactionAsync();
        transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test" });

        transaction.Rollback();

        Assert.Empty(transaction.PendingOperations);
        Assert.True(transaction.IsCompleted);
    }

    [Fact]
    public async Task RollbackAsync_ClearsPendingOperations()
    {
        var transaction = await CreateTransactionAsync();
        transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test" });

        await transaction.RollbackAsync();

        Assert.Empty(transaction.PendingOperations);
        Assert.True(transaction.IsCompleted);
    }

    [Fact]
    public async Task EnqueueInsert_AfterRollback_ThrowsInvalidOperationException()
    {
        var transaction = await CreateTransactionAsync();
        transaction.Rollback();

        Assert.Throws<InvalidOperationException>(() =>
            transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test" }));
    }

    [Fact]
    public async Task EnqueueUpsert_AfterRollback_ThrowsInvalidOperationException()
    {
        var transaction = await CreateTransactionAsync();
        transaction.Rollback();

        Assert.Throws<InvalidOperationException>(() =>
            transaction.EnqueueUpsert(_mockCollection.Object, "doc1", new { Name = "Test" }));
    }

    [Fact]
    public async Task EnqueueRemove_AfterRollback_ThrowsInvalidOperationException()
    {
        var transaction = await CreateTransactionAsync();
        transaction.Rollback();

        Assert.Throws<InvalidOperationException>(() =>
            transaction.EnqueueRemove(_mockCollection.Object, "doc1"));
    }

    [Fact]
    public async Task Rollback_AfterRollback_ThrowsInvalidOperationException()
    {
        var transaction = await CreateTransactionAsync();
        transaction.Rollback();

        Assert.Throws<InvalidOperationException>(() => transaction.Rollback());
    }

    [Fact]
    public async Task Dispose_ClearsPendingOperations_IfNotCompleted()
    {
        var transaction = await CreateTransactionAsync();
        transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test" });

        transaction.Dispose();

        Assert.Empty(transaction.PendingOperations);
    }

    [Fact]
    public async Task DisposeAsync_ClearsPendingOperations_IfNotCompleted()
    {
        var transaction = await CreateTransactionAsync();
        transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test" });

        await transaction.DisposeAsync();

        Assert.Empty(transaction.PendingOperations);
    }

    [Fact]
    public async Task EnqueueInsert_AfterDispose_ThrowsObjectDisposedException()
    {
        var transaction = await CreateTransactionAsync();
        transaction.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            transaction.EnqueueInsert(_mockCollection.Object, "doc1", new { Name = "Test" }));
    }

    [Fact]
    public async Task CommitAsync_WithNoOperations_CompletesSuccessfully()
    {
        var transaction = await CreateTransactionAsync();

        await transaction.CommitAsync();

        Assert.True(transaction.IsCompleted);
    }

    [Fact]
    public async Task IsCompleted_InitiallyFalse()
    {
        var transaction = await CreateTransactionAsync();

        Assert.False(transaction.IsCompleted);
    }

    [Fact]
    public async Task IsCompleted_TrueAfterRollback()
    {
        var transaction = await CreateTransactionAsync();

        transaction.Rollback();

        Assert.True(transaction.IsCompleted);
    }
}
