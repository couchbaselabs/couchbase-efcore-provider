using System.Data;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseCommandTests(
    BloggingFixture bloggingFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task CreateCommand_ReturnsValidCouchbaseCommand()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();

        Assert.NotNull(command);
        Assert.IsType<CouchbaseCommand>(command);
    }

    [Fact]
    public async Task CommandText_CanBeSetAndRetrieved()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        Assert.Equal("SELECT 1", command.CommandText);
    }

    [Fact]
    public async Task CommandTimeout_CanBeSetAndRetrieved()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandTimeout = 60;

        Assert.Equal(60, command.CommandTimeout);
    }

    [Fact]
    public async Task CreateParameter_ReturnsCouchbaseParameter()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        var parameter = command.CreateParameter();

        Assert.NotNull(parameter);
        Assert.IsType<CouchbaseParameter>(parameter);
    }

    [Fact]
    public async Task Parameters_CanAddAndRetrieve()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = (CouchbaseCommand)connection.CreateCommand();
        command.Parameters.AddWithValue("$param1", "value1");
        command.Parameters.AddWithValue("$param2", 42);

        Assert.Equal(2, command.Parameters.Count);
    }

    [Fact]
    public async Task Prepare_DoesNotThrow()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM `default`.`blogs`.`blog`";

        var exception = Record.Exception(() => command.Prepare());

        Assert.Null(exception);
    }

    [Fact]
    public async Task PrepareAsync_DoesNotThrow()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM `default`.`blogs`.`blog`";

        var exception = await Record.ExceptionAsync(() => command.PrepareAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithSelectQuery_ReturnsResult()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RAW 1";

        var result = await command.ExecuteNonQueryAsync(CancellationToken.None);

        // SELECT queries don't mutate, so result should be 0 or -1
        Assert.True(result is 0 or -1, $"Expected ExecuteNonQueryAsync to return 0 or -1 for a SELECT query, but got {result}.");
        outputHelper.WriteLine($"ExecuteNonQueryAsync result: {result}");
    }

    [Fact]
    public async Task ExecuteNonQuery_WithSelectQuery_ReturnsResult()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RAW 1";

        var result = command.ExecuteNonQuery();

        Assert.True(result is 0 or -1, $"Expected ExecuteNonQuery to return 0 or -1 for a SELECT query, but got {result}.");
        outputHelper.WriteLine($"ExecuteNonQuery result: {result}");
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithSimpleQuery_ReturnsValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RAW 42";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(42L, result);
        outputHelper.WriteLine($"ExecuteScalarAsync result: {result} (type: {result?.GetType().Name})");
    }

    [Fact]
    public async Task ExecuteScalar_WithSimpleQuery_ReturnsValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'hello' AS greeting";

        var result = command.ExecuteScalar();

        Assert.NotNull(result);
        Assert.Equal("hello", result);
        outputHelper.WriteLine($"ExecuteScalar result: {result}");
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithCountQuery_ReturnsCount()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) AS cnt FROM `default`.`blogs`.`blog`";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        outputHelper.WriteLine($"Blog count: {result}");
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithNoResults_ReturnsNull()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b.ID  FROM `default`.`blogs`.`blog` as b WHERE blogId = -99999";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteReaderAsync_WithSelectQuery_ReturnsDataReader()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM `default`.`blogs`.`blog` LIMIT 5";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.NotNull(reader);
        Assert.IsType<CouchbaseDbDataReader<object>>(reader);
    }

    [Fact]
    public async Task ExecuteReader_WithSelectQuery_ReturnsDataReader()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM `default`.`blogs`.`blog` LIMIT 5";

        using var reader = command.ExecuteReader();

        Assert.NotNull(reader);
        Assert.IsType<CouchbaseDbDataReader<object>>(reader);
    }

    [Fact]
    public async Task ExecuteReaderAsync_HasRows_ReturnsTrue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM `default`.`blogs`.`blog` LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithParameters_UsesParameterValues()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = (CouchbaseCommand)connection.CreateCommand();
        command.CommandText = "SELECT $value AS result";
        command.Parameters.AddWithValue("$value", 123);

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(123L, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithMultipleParameters_UsesAllParameters()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = (CouchbaseCommand)connection.CreateCommand();
        command.CommandText = "SELECT $a + $b AS sum";
        command.Parameters.AddWithValue("$a", 10);
        command.Parameters.AddWithValue("$b", 20);

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(30L, result);
    }

    [Fact]
    public async Task Cancel_BeforeExecution_DoesNotThrow()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        // Cancel should not throw even if called before execution
        var exception = Record.Exception(() => command.Cancel());

        Assert.Null(exception);
    }

    [Fact]
    public async Task CommandType_DefaultsToText()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();

        Assert.Equal(CommandType.Text, command.CommandType);
    }

    [Fact]
    public async Task Connection_IsSetCorrectly()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();

        Assert.Same(connection, command.Connection);
    }

    [Fact]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();

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
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await command.DisposeAsync();
            await command.DisposeAsync();
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_WithTimeout_DoesNotThrowForFastQuery()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = 30;

        var exception = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithNullResult_ReturnsDBNull()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT NULL AS nullValue";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithBooleanResult_ReturnsBoolean()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TRUE AS boolValue";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.True((bool)result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithStringResult_ReturnsString()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'test string' AS strValue";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("test string", result);
    }

    [Fact]
    public async Task ExecuteScalarAsync_WithDecimalResult_ReturnsNumber()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 3.14159 AS piValue";

        var result = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3.14159, Convert.ToDouble(result), 5);
    }

    [Fact]
    public async Task ExecuteReaderAsync_CanReadRows()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT blogId, url FROM `default`.`blogs`.`blog` LIMIT 2";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var hasRows = reader.Read();
        Assert.True(hasRows);
        outputHelper.WriteLine("Successfully read first row from reader");
    }
}
