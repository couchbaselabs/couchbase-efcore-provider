using System.Data;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class ConnectionTests(
    BloggingFixture bloggingFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task GetDbConnection_ReturnsWorkingConnection()
    {
        await using var context = bloggingFixture.GetDbContext();

        var connection = context.Database.GetDbConnection();

        Assert.NotNull(connection);
        Assert.IsType<CouchbaseConnection>(connection);
    }

    [Fact]
    public async Task GetDbConnection_ConnectionString_IsNotEmpty()
    {
        await using var context = bloggingFixture.GetDbContext();

        var connection = context.Database.GetDbConnection();

        Assert.NotNull(connection.ConnectionString);
        Assert.NotEmpty(connection.ConnectionString);
        Assert.Contains("couchbase", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDbConnection_Database_ReturnsBucketName()
    {
        await using var context = bloggingFixture.GetDbContext();

        var connection = context.Database.GetDbConnection();

        Assert.NotNull(connection.Database);
        Assert.NotEmpty(connection.Database);
    }

    [Fact]
    public async Task OpenAsync_SetsStateToOpen()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        await connection.OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CloseAsync_SetsStateToClosed()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        await connection.OpenAsync();
        await connection.CloseAsync();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task CreateCommand_ReturnsWorkingCommand()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();

        Assert.NotNull(command);
        Assert.IsType<CouchbaseCommand>(command);
        Assert.Same(connection, command.Connection);
    }

    [Fact]
    public async Task BeginTransaction_WhenOpen_ReturnsCouchbaseDbTransaction()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var transaction = connection.BeginTransaction();

        Assert.NotNull(transaction);
        Assert.IsType<CouchbaseDbTransaction>(transaction);
    }

    [Fact]
    public async Task BeginTransaction_WhenClosed_ThrowsInvalidOperationException()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
    }

    [Fact]
    public async Task BeginTransaction_WhenTransactionActive_ThrowsInvalidOperationException()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        var transaction = connection.BeginTransaction();

        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());

        transaction.Dispose();
    }

    [Fact]
    public async Task BeginTransaction_AfterRollback_CanStartNewTransaction()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var firstTransaction = connection.BeginTransaction();
        firstTransaction.Rollback();
        firstTransaction.Dispose();

        var secondTransaction = connection.BeginTransaction();
        Assert.NotNull(secondTransaction);
        secondTransaction.Dispose();
    }

    [Fact]
    public async Task Transaction_Rollback_ClearsTransactionState()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var transaction = (CouchbaseDbTransaction)connection.BeginTransaction();
        transaction.Rollback();

        Assert.True(transaction.IsCompleted);
        Assert.Empty(transaction.PendingOperations);
    }

    [Fact]
    public async Task DataSource_ReturnsConnectionHost()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        Assert.NotNull(connection.DataSource);
        outputHelper.WriteLine($"DataSource: {connection.DataSource}");
    }

    [Fact]
    public async Task ServerVersion_ReturnsCouchbase()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        Assert.NotNull(connection.ServerVersion);
        Assert.Equal("Couchbase", connection.ServerVersion);
    }

    [Fact]
    public async Task ChangeDatabase_ThrowsNotSupportedException()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        Assert.Throws<NotSupportedException>(() => connection.ChangeDatabase("other-bucket"));
    }

    [Fact]
    public async Task ConnectionString_SetThrowsNotSupportedException()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();

        Assert.Throws<NotSupportedException>(() => connection.ConnectionString = "new-connection");
    }
}
