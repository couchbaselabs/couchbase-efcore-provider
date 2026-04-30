using System.Text.Json;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Query;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDbDataReaderTests
{
    [Fact]
    public void Constructor_WithNullQueryResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CouchbaseDbDataReader<object>(null!));
    }

    [Fact]
    public async Task Read_WithRows_ReturnsTrue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Read_WithNoRows_ReturnsFalse()
    {
        var reader = CreateReader(new List<JsonElement>());

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Read_MultipleRows_IteratesCorrectly()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement,
            JsonDocument.Parse("{\"id\": 3}").RootElement
        };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(3L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FieldCount_ReturnsCorrectCount()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\", \"active\": true}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(3, reader.FieldCount);
    }

    [Fact]
    public async Task GetName_ReturnsFieldName()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("id", reader.GetName(0));
        Assert.Equal("name", reader.GetName(1));
    }

    [Fact]
    public async Task GetOrdinal_ReturnsCorrectOrdinal()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("id"));
        Assert.Equal(1, reader.GetOrdinal("name"));
    }

    [Fact]
    public async Task GetOrdinal_CaseInsensitive()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"Name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("name"));
        Assert.Equal(0, reader.GetOrdinal("NAME"));
        Assert.Equal(0, reader.GetOrdinal("Name"));
    }

    [Fact]
    public async Task GetOrdinal_InvalidName_ThrowsIndexOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("invalid"));
    }

    [Fact]
    public void GetOrdinal_NullName_ThrowsArgumentNullException()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.Throws<ArgumentNullException>(() => reader.GetOrdinal(null!));
    }

    [Fact]
    public async Task GetValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 42, \"name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetValue(0));
        Assert.Equal("test", reader.GetValue(1));
    }

    [Fact]
    public async Task GetValue_WithNullValue_ReturnsDBNull()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"value\": null}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, reader.GetValue(0));
    }

    [Fact]
    public void GetValue_OnEmptyResult_ThrowsIndexOutOfRangeException()
    {
        var reader = CreateReader(new List<JsonElement>());

        // EnsureFieldInfo will try to read, find no rows, so field count is 0
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_InvalidOrdinal_ThrowsIndexOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(99));
    }

    [Fact]
    public async Task GetBoolean_ReturnsBoolean()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"active\": true, \"deleted\": false}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.True(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
    }

    [Fact]
    public async Task GetInt32_ReturnsInt()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"count\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42, reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt64_ReturnsLong()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"bigNumber\": 9223372036854775807}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(9223372036854775807L, reader.GetInt64(0));
    }

    [Fact]
    public async Task GetDouble_ReturnsDouble()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"price\": 19.99}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(19.99, reader.GetDouble(0), 2);
    }

    [Fact]
    public async Task GetDecimal_ReturnsDecimal()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"amount\": 123.45}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(123.45m, reader.GetDecimal(0));
    }

    [Fact]
    public async Task GetString_ReturnsString()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"name\": \"Hello World\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("Hello World", reader.GetString(0));
    }

    [Fact]
    public async Task GetDateTime_ReturnsDateTime()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"created\": \"2025-01-15T10:30:00Z\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetDateTime(0);
        Assert.Equal(2025, result.Year);
        Assert.Equal(1, result.Month);
        Assert.Equal(15, result.Day);
    }

    [Fact]
    public async Task GetGuid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();
        var rows = new List<JsonElement> { JsonDocument.Parse($"{{\"id\": \"{guid}\"}}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(guid, reader.GetGuid(0));
    }

    [Fact]
    public async Task IsDBNull_WithNull_ReturnsTrue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"value\": null}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task IsDBNull_WithValue_ReturnsFalse()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"value\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.False(reader.IsDBNull(0));
    }

    [Fact]
    public async Task GetValues_FillsArray()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\", \"active\": true}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var values = new object[3];
        var count = reader.GetValues(values);

        Assert.Equal(3, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal("test", values[1]);
        Assert.True((bool)values[2]);
    }

    [Fact]
    public async Task GetValues_WithSmallerArray_FillsPartially()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\", \"active\": true}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var values = new object[2];
        var count = reader.GetValues(values);

        Assert.Equal(2, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal("test", values[1]);
    }

    [Fact]
    public async Task Indexer_ByOrdinal_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader[0]);
    }

    [Fact]
    public async Task Indexer_ByName_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader["id"]);
    }

    [Fact]
    public void IsClosed_InitiallyFalse()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.False(reader.IsClosed);
    }

    [Fact]
    public void Close_SetsIsClosed()
    {
        var reader = CreateReader(new List<JsonElement>());

        reader.Close();

        Assert.True(reader.IsClosed);
    }

    [Fact]
    public async Task CloseAsync_SetsIsClosed()
    {
        var reader = CreateReader(new List<JsonElement>());

        await reader.CloseAsync();

        Assert.True(reader.IsClosed);
    }

    [Fact]
    public async Task Read_AfterClose_ReturnsFalse()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        reader.Close();

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void NextResult_ReturnsFalse()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.False(reader.NextResult());
    }

    [Fact]
    public async Task NextResultAsync_ReturnsFalse()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.False(await reader.NextResultAsync(CancellationToken.None));
    }

    [Fact]
    public void Depth_ReturnsZero()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.Equal(0, reader.Depth);
    }

    [Fact]
    public void RecordsAffected_ReturnsNegativeOne()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public void HasRows_WithSuccessStatus_ReturnsTrue()
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Success };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new List<JsonElement>().ToAsyncEnumerable().GetAsyncEnumerator());

        var reader = new CouchbaseDbDataReader<JsonElement>(mockQueryResult.Object);

        Assert.True(reader.HasRows);
    }

    [Fact]
    public void HasRows_WithErrorStatus_ReturnsFalse()
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Errors };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new List<JsonElement>().ToAsyncEnumerable().GetAsyncEnumerator());

        var reader = new CouchbaseDbDataReader<JsonElement>(mockQueryResult.Object);

        Assert.False(reader.HasRows);
    }

    [Fact]
    public async Task GetSchemaTable_ReturnsSchemaTable()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1, \"name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var schemaTable = reader.GetSchemaTable();

        Assert.NotNull(schemaTable);
        Assert.Equal(2, schemaTable.Rows.Count);
        Assert.Equal("id", schemaTable.Rows[0]["ColumnName"]);
        Assert.Equal(0, schemaTable.Rows[0]["ColumnOrdinal"]);
        Assert.Equal("name", schemaTable.Rows[1]["ColumnName"]);
        Assert.Equal(1, schemaTable.Rows[1]["ColumnOrdinal"]);
    }

    [Fact]
    public async Task GetDataTypeName_ReturnsTypeName()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"count\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("Int64", reader.GetDataTypeName(0));
    }

    [Fact]
    public async Task GetFieldType_ReturnsType()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"count\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(typeof(long), reader.GetFieldType(0));
    }

    [Fact]
    public async Task GetFieldValue_Generic_ReturnsTypedValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"count\": 42}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetFieldValue<long>(0));
    }

    [Fact]
    public async Task GetChar_ReturnsFirstChar()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"letter\": \"A\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal('A', reader.GetChar(0));
    }

    [Fact]
    public async Task GetByte_ReturnsByte()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"value\": 255}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((byte)255, reader.GetByte(0));
    }

    [Fact]
    public async Task GetInt16_ReturnsShort()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"value\": 32767}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((short)32767, reader.GetInt16(0));
    }

    [Fact]
    public async Task GetFloat_ReturnsFloat()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"value\": 3.14}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(3.14f, reader.GetFloat(0), 2);
    }

    [Fact]
    public async Task Dispose_ClosesReader()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        reader.Dispose();

        Assert.True(reader.IsClosed);
    }

    [Fact]
    public async Task DisposeAsync_ClosesReader()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        await reader.DisposeAsync();

        Assert.True(reader.IsClosed);
    }

    [Fact]
    public void GetEnumerator_ReturnsEnumerator()
    {
        var reader = CreateReader(new List<JsonElement>());

        var enumerator = reader.GetEnumerator();

        Assert.NotNull(enumerator);
    }

    private static CouchbaseDbDataReader<JsonElement> CreateReader(List<JsonElement> rows)
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Success };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(rows.ToAsyncEnumerable().GetAsyncEnumerator());

        return new CouchbaseDbDataReader<JsonElement>(mockQueryResult.Object);
    }
}
