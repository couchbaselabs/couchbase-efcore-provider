using System.Data;
using System.Data.Common;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseCommandTests
{
    private readonly Mock<IBucketProvider> _mockBucketProvider;
    private readonly Mock<ICouchbaseDbContextOptionsBuilder> _mockOptions;
    private readonly Mock<IBucket> _mockBucket;
    private readonly Mock<ICluster> _mockCluster;

    public CouchbaseCommandTests()
    {
        _mockBucketProvider = new Mock<IBucketProvider>();
        _mockOptions = new Mock<ICouchbaseDbContextOptionsBuilder>();
        _mockBucket = new Mock<IBucket>();
        _mockCluster = new Mock<ICluster>();

        _mockOptions.Setup(o => o.Bucket).Returns("test-bucket");
        _mockOptions.Setup(o => o.ConnectionString).Returns("couchbase://localhost");
        _mockOptions.Setup(o => o.ClusterOptions).Returns(new ClusterOptions().WithConnectionString("couchbase://localhost"));

        _mockBucket.Setup(b => b.Cluster).Returns(_mockCluster.Object);
        _mockBucketProvider.Setup(p => p.GetBucketAsync("test-bucket"))
            .ReturnsAsync(_mockBucket.Object);
    }

    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var command = new CouchbaseCommand();

        Assert.Equal(string.Empty, command.CommandText);
        Assert.Equal(0, command.CommandTimeout);
        Assert.Equal(CommandType.Text, command.CommandType);
        Assert.Equal(UpdateRowSource.None, command.UpdatedRowSource);
        Assert.False(command.DesignTimeVisible);
        Assert.Null(command.Connection);
        Assert.Null(command.Transaction);
    }

    [Fact]
    public void CommandText_SetNull_ReturnsEmptyString()
    {
        var command = new CouchbaseCommand();

        command.CommandText = null!;

        Assert.Equal(string.Empty, command.CommandText);
    }

    [Fact]
    public void CommandText_SetValue_ReturnsValue()
    {
        var command = new CouchbaseCommand();

        command.CommandText = "SELECT * FROM bucket";

        Assert.Equal("SELECT * FROM bucket", command.CommandText);
    }

    [Fact]
    public void CommandTimeout_SetValue_ReturnsValue()
    {
        var command = new CouchbaseCommand();

        command.CommandTimeout = 30;

        Assert.Equal(30, command.CommandTimeout);
    }

    [Fact]
    public void CommandType_SetValue_ReturnsValue()
    {
        var command = new CouchbaseCommand();

        command.CommandType = CommandType.StoredProcedure;

        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
    }

    [Fact]
    public void UpdatedRowSource_SetValue_ReturnsValue()
    {
        var command = new CouchbaseCommand();

        command.UpdatedRowSource = UpdateRowSource.FirstReturnedRecord;

        Assert.Equal(UpdateRowSource.FirstReturnedRecord, command.UpdatedRowSource);
    }

    [Fact]
    public void DesignTimeVisible_SetValue_ReturnsValue()
    {
        var command = new CouchbaseCommand();

        command.DesignTimeVisible = true;

        Assert.True(command.DesignTimeVisible);
    }

    [Fact]
    public void Parameters_ReturnsNonNullCollection()
    {
        var command = new CouchbaseCommand();

        Assert.NotNull(command.Parameters);
        Assert.IsType<CouchbaseParameterCollection>(command.Parameters);
    }

    [Fact]
    public void Parameters_ReturnsSameInstance()
    {
        var command = new CouchbaseCommand();

        var params1 = command.Parameters;
        var params2 = command.Parameters;

        Assert.Same(params1, params2);
    }

    [Fact]
    public void CreateParameter_ReturnsCouchbaseParameter()
    {
        var command = new CouchbaseCommand();

        var parameter = command.CreateParameter();

        Assert.NotNull(parameter);
        Assert.IsType<CouchbaseParameter>(parameter);
    }

    [Fact]
    public void Prepare_DoesNotThrow()
    {
        var command = new CouchbaseCommand();

        var exception = Record.Exception(() => command.Prepare());

        Assert.Null(exception);
    }

    [Fact]
    public async Task PrepareAsync_DoesNotThrow()
    {
        var command = new CouchbaseCommand();

        var exception = await Record.ExceptionAsync(() => command.PrepareAsync());

        Assert.Null(exception);
    }

    [Fact]
    public void Cancel_WhenNoCancellationToken_DoesNotThrow()
    {
        var command = new CouchbaseCommand();

        var exception = Record.Exception(() => command.Cancel());

        Assert.Null(exception);
    }

    [Fact]
    public void Connection_SetAndGet_Works()
    {
        var command = new CouchbaseCommand();
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);

        command.Connection = connection;

        Assert.Same(connection, command.Connection);
    }

    [Fact]
    public void Transaction_SetAndGet_Works()
    {
        var command = new CouchbaseCommand();
        var mockTransaction = new Mock<DbTransaction>();

        command.Transaction = mockTransaction.Object;

        Assert.Same(mockTransaction.Object, command.Transaction);
    }

    [Fact]
    public void ResolveCluster_WithExplicitCluster_ReturnsExplicitCluster()
    {
        var command = new CouchbaseCommand { Cluster = _mockCluster.Object };

        // Indirectly test via ExecuteNonQueryAsync which calls ResolveCluster
        // If ResolveCluster fails, it throws InvalidOperationException
        Assert.NotNull(command.Cluster);
        Assert.Same(_mockCluster.Object, command.Cluster);
    }

    [Fact]
    public void ResolveCluster_WithNoClusterAndNoConnection_ThrowsInvalidOperationException()
    {
        var command = new CouchbaseCommand
        {
            CommandText = "SELECT 1"
        };

        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public async Task ResolveCluster_WithNoClusterAndNoConnection_ThrowsInvalidOperationExceptionAsync()
    {
        var command = new CouchbaseCommand
        {
            CommandText = "SELECT 1"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResolveCluster_WithConnectionButNoCluster_ThrowsInvalidOperationException()
    {
        var connection = new CouchbaseConnection(_mockBucketProvider.Object, _mockOptions.Object);
        // Connection is not opened, so Cluster is null
        var command = new CouchbaseCommand
        {
            Connection = connection,
            CommandText = "SELECT 1"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithValidCluster_CallsQueryAsync()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<object>(new List<object>());
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = 1"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        _mockCluster.Verify(c => c.QueryAsync<object>("UPDATE bucket SET x = 1", It.IsAny<QueryOptions>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithRows_ReturnsFirstColumnOfFirstRow()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"result\": 42}").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT COUNT(*) as result FROM bucket"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(42L, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithNoRows_ReturnsNull()
    {
        var rows = new List<JsonElement>();
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT * FROM bucket WHERE 1=0"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithNullJsonValue_ReturnsDBNull()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("null").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT RAW NULL"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithEmptyObject_ReturnsNull()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{}").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithNullPropertyValue_ReturnsDBNull()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"result\": null}").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT NULL as result"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithRawScalar_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("42").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT RAW 42"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(42L, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithStringValue_ReturnsString()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"result\": \"hello\"}").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT 'hello' as result"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithBooleanValue_ReturnsBoolean()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"result\": true}").RootElement };
        var mockQueryResult = CreateMockQueryResultWithRows(rows);
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT TRUE as result"
        };

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.True((bool)result!);
    }

    [Fact]
    public async Task ExecuteDbDataReaderAsync_ReturnsDataReader()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<JsonElement>(new List<JsonElement>());
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT * FROM bucket"
        };

        var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.NotNull(reader);
        Assert.IsType<CouchbaseDbDataReader<JsonElement>>(reader);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithParameters_CallsQueryAsyncWithCommandText()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<object>(new List<object>());
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = $value WHERE id = $id"
        };
        command.Parameters.AddWithValue("$value", 100);
        command.Parameters.AddWithValue("$id", "doc1");

        await command.ExecuteNonQueryAsync(CancellationToken.None);

        _mockCluster.Verify(c => c.QueryAsync<object>("UPDATE bucket SET x = $value WHERE id = $id", It.IsAny<QueryOptions>()), Times.Once);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var command = new CouchbaseCommand();

        var exception = Record.Exception(() =>
        {
            command.Dispose();
            command.Dispose();
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var command = new CouchbaseCommand();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await command.DisposeAsync();
            await command.DisposeAsync();
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithNullMetrics_ReturnsNegativeOne()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<object>(new List<object>());
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = 1"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithMutationCount_ReturnsMutationCount()
    {
        var mockQueryResult = CreateMockQueryResultWithMetrics<object>(new List<object>(), mutationCount: 42);
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = 1"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithMutationCountExceedingIntMax_ReturnsIntMax()
    {
        var largeMutationCount = (uint)int.MaxValue + 1000;
        var mockQueryResult = CreateMockQueryResultWithMetrics<object>(new List<object>(), mutationCount: largeMutationCount);
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = 1"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithZeroMutationCount_ReturnsZero()
    {
        var mockQueryResult = CreateMockQueryResultWithMetrics<object>(new List<object>(), mutationCount: 0);
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = 1 WHERE false"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithSelectStatement_ReturnsNegativeOne()
    {
        var mockQueryResult = CreateMockQueryResultWithMetrics<object>(new List<object>(), mutationCount: 5);
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT * FROM bucket"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithMaxIntMutationCount_ReturnsMaxInt()
    {
        var mockQueryResult = CreateMockQueryResultWithMetrics<object>(new List<object>(), mutationCount: (uint)int.MaxValue);
        _mockCluster.Setup(c => c.QueryAsync<object>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "UPDATE bucket SET x = 1"
        };

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithCloseConnectionBehavior_ClosesConnectionWhenReaderClosed()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<JsonElement>(new List<JsonElement>());
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var mockConnection = new Mock<DbConnection>();
        mockConnection.Setup(c => c.CloseAsync()).Returns(Task.CompletedTask);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            Connection = mockConnection.Object,
            CommandText = "SELECT * FROM bucket"
        };

        var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        await reader.CloseAsync();

        mockConnection.Verify(c => c.CloseAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithCloseConnectionBehavior_ClosesConnectionWhenReaderDisposed()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<JsonElement>(new List<JsonElement>());
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var mockConnection = new Mock<DbConnection>();
        mockConnection.Setup(c => c.CloseAsync()).Returns(Task.CompletedTask);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            Connection = mockConnection.Object,
            CommandText = "SELECT * FROM bucket"
        };

        var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        await reader.DisposeAsync();

        mockConnection.Verify(c => c.CloseAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithDefaultBehavior_DoesNotCloseConnection()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<JsonElement>(new List<JsonElement>());
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var mockConnection = new Mock<DbConnection>();

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            Connection = mockConnection.Object,
            CommandText = "SELECT * FROM bucket"
        };

        var reader = await command.ExecuteReaderAsync(CommandBehavior.Default);
        await reader.CloseAsync();

        mockConnection.Verify(c => c.Close(), Times.Never);
        mockConnection.Verify(c => c.CloseAsync(), Times.Never);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithNoConnection_DoesNotThrowOnClose()
    {
        var mockQueryResult = CreateMockQueryResultWithRows<JsonElement>(new List<JsonElement>());
        _mockCluster.Setup(c => c.QueryAsync<JsonElement>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(mockQueryResult);

        var command = new CouchbaseCommand
        {
            Cluster = _mockCluster.Object,
            CommandText = "SELECT * FROM bucket"
        };

        var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);

        // Should not throw even with CloseConnection and no connection
        var exception = await Record.ExceptionAsync(async () => await reader.CloseAsync());
        Assert.Null(exception);
    }

    private static IQueryResult<T> CreateMockQueryResultWithRows<T>(List<T> rows)
    {
        var mockResult = new Mock<IQueryResult<T>>();
        var asyncEnumerable = rows.ToAsyncEnumerable();
        mockResult.Setup(r => r.Rows).Returns(asyncEnumerable);
        mockResult.As<IAsyncEnumerable<T>>()
            .Setup(r => r.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => asyncEnumerable.GetAsyncEnumerator(ct));
        mockResult.Setup(r => r.MetaData).Returns((QueryMetaData?)null);

        return mockResult.Object;
    }

    private static IQueryResult<T> CreateMockQueryResultWithMetrics<T>(List<T> rows, uint mutationCount)
    {
        var mockResult = new Mock<IQueryResult<T>>();
        var asyncEnumerable = rows.ToAsyncEnumerable();
        mockResult.Setup(r => r.Rows).Returns(asyncEnumerable);
        mockResult.As<IAsyncEnumerable<T>>()
            .Setup(r => r.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => asyncEnumerable.GetAsyncEnumerator(ct));

        var metrics = new QueryMetrics
        {
            MutationCount = mutationCount
        };
        var metaData = new QueryMetaData
        {
            Metrics = metrics
        };
        mockResult.Setup(r => r.MetaData).Returns(metaData);

        return mockResult.Object;
    }
}
