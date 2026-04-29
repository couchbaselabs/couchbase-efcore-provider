using System.Data;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnectionTests
{
    private readonly Mock<IBucketProvider> _mockBucketProvider;
    private readonly Mock<ICouchbaseDbContextOptionsBuilder> _mockOptions;
    private readonly Mock<IBucket> _mockBucket;
    private readonly Mock<ICluster> _mockCluster;

    public CouchbaseConnectionTests()
    {
        _mockBucketProvider = new Mock<IBucketProvider>();
        _mockOptions = new Mock<ICouchbaseDbContextOptionsBuilder>();
        _mockBucket = new Mock<IBucket>();
        _mockCluster = new Mock<ICluster>();

        _mockOptions.Setup(o => o.Bucket).Returns("test-bucket");
        _mockOptions.Setup(o => o.ConnectionString).Returns("couchbase://localhost?bucket=test-bucket");
        _mockOptions.Setup(o => o.ClusterOptions).Returns(new ClusterOptions().WithConnectionString("couchbase://localhost"));

        _mockBucket.Setup(b => b.Cluster).Returns(_mockCluster.Object);
        _mockBucketProvider.Setup(p => p.GetBucketAsync("test-bucket"))
            .ReturnsAsync(_mockBucket.Object);
    }

    [Fact]
    public void Constructor_SetsInitialState_ToClosed()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void ConnectionString_ReturnsExpectedValue()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Equal("couchbase://localhost?bucket=test-bucket", connection.ConnectionString);
    }

    [Fact]
    public void ConnectionString_Set_ThrowsNotSupportedException()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Throws<NotSupportedException>(() => connection.ConnectionString = "new-connection-string");
    }

    [Fact]
    public void Database_ReturnsExpectedBucketName()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Equal("test-bucket", connection.Database);
    }

    [Fact]
    public async Task OpenAsync_SetsState_ToOpen()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        await connection.OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task OpenAsync_SetsClusterInstance()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        await connection.OpenAsync();

        Assert.NotNull(connection.Cluster);
        Assert.Same(_mockCluster.Object, connection.Cluster);
    }

    [Fact]
    public async Task OpenAsync_WhenAlreadyOpen_DoesNotReopenConnection()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        await connection.OpenAsync();
        await connection.OpenAsync();

        _mockBucketProvider.Verify(p => p.GetBucketAsync("test-bucket"), Times.Once);
    }

    [Fact]
    public async Task Close_SetsState_ToClosed()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        connection.Close();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task CloseAsync_SetsState_ToClosed()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        await connection.CloseAsync();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void ChangeDatabase_ThrowsNotSupportedException()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Throws<NotSupportedException>(() => connection.ChangeDatabase("other-bucket"));
    }

    [Fact]
    public void BeginTransaction_WhenConnectionNotOpen_ThrowsInvalidOperationException()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
    }

    [Fact]
    public async Task BeginTransaction_WhenConnectionOpen_ReturnsCouchbaseDbTransaction()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        var transaction = connection.BeginTransaction();

        Assert.NotNull(transaction);
        Assert.IsType<CouchbaseDbTransaction>(transaction);
    }

    [Fact]
    public async Task BeginTransaction_WhenTransactionAlreadyActive_ThrowsInvalidOperationException()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();
        var firstTransaction = connection.BeginTransaction();

        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
    }

    [Fact]
    public async Task BeginTransaction_AfterPreviousTransactionCompletes_Succeeds()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        var firstTransaction = connection.BeginTransaction();
        firstTransaction.Rollback();

        var secondTransaction = connection.BeginTransaction();

        Assert.NotNull(secondTransaction);
    }

    [Fact]
    public async Task CreateCommand_ReturnsValidCommand()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        var command = connection.CreateCommand();

        Assert.NotNull(command);
        Assert.IsType<CouchbaseCommand>(command);
        Assert.Same(connection, command.Connection);
    }

    [Fact]
    public void DataSource_ReturnsConnectionStringHost()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.NotNull(connection.DataSource);
        Assert.Contains("localhost", connection.DataSource);
    }

    [Fact]
    public void ServerVersion_ReturnsCouchbase()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        Assert.Equal("Couchbase", connection.ServerVersion);
    }

    [Fact]
    public async Task Dispose_ClosesConnection()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        connection.Dispose();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task DisposeAsync_ClosesConnection()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        await connection.OpenAsync();

        await connection.DisposeAsync();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }
}
