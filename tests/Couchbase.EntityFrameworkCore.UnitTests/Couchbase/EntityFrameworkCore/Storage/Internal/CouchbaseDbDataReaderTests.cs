using System.Data;
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
        Assert.Throws<ArgumentNullException>(() =>
            new CouchbaseDbDataReader<object>(null!, Array.Empty<string?>()));
    }

    [Fact]
    public async Task Constructor_WithNullColumnNames_FallsBackToPositionalPath()
    {
        // null columnNames is valid — it means "no alias mapping; use positional resolution".
        // This mirrors the raw ADO.NET path and must not throw at construction or at read time.
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        mockQueryResult.Setup(q => q.Rows).Returns(rows.ToAsyncEnumerable());

        var reader = new CouchbaseDbDataReader<JsonElement>(mockQueryResult.Object, (string?[]?)null);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));
    }

    [Fact]
    public async Task Read_WithRows_ReturnsTrue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\"}") };
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
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}"),
            ParseElement("{\"id\": 3}")
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
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\", \"active\": true}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(3, reader.FieldCount);
    }

    [Fact]
    public async Task GetName_ReturnsFieldName()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("id", reader.GetName(0));
        Assert.Equal("name", reader.GetName(1));
    }

    [Fact]
    public async Task GetOrdinal_ReturnsCorrectOrdinal()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("id"));
        Assert.Equal(1, reader.GetOrdinal("name"));
    }

    [Fact]
    public async Task GetOrdinal_CaseInsensitive()
    {
        var rows = new List<JsonElement> { ParseElement("{\"Name\": \"test\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("name"));
        Assert.Equal(0, reader.GetOrdinal("NAME"));
        Assert.Equal(0, reader.GetOrdinal("Name"));
    }

    [Fact]
    public async Task GetOrdinal_InvalidName_ThrowsIndexOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
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
        var rows = new List<JsonElement> { ParseElement("{\"id\": 42, \"name\": \"test\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetValue(0));
        Assert.Equal("test", reader.GetValue(1));
    }

    [Fact]
    public async Task GetValue_WithNullValue_ReturnsDBNull()
    {
        var rows = new List<JsonElement> { ParseElement("{\"value\": null}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, reader.GetValue(0));
    }

    [Fact]
    public void GetValue_BeforeRead_ThrowsInvalidOperationException()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_InvalidOrdinal_ThrowsIndexOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(99));
    }

    [Fact]
    public async Task GetBoolean_ReturnsBoolean()
    {
        var rows = new List<JsonElement> { ParseElement("{\"active\": true, \"deleted\": false}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.True(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
    }

    [Fact]
    public async Task GetInt32_ReturnsInt()
    {
        var rows = new List<JsonElement> { ParseElement("{\"count\": 42}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42, reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt64_ReturnsLong()
    {
        var rows = new List<JsonElement> { ParseElement("{\"bigNumber\": 9223372036854775807}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(9223372036854775807L, reader.GetInt64(0));
    }

    [Fact]
    public async Task GetDouble_ReturnsDouble()
    {
        var rows = new List<JsonElement> { ParseElement("{\"price\": 19.99}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(19.99, reader.GetDouble(0), 2);
    }

    [Fact]
    public async Task GetDecimal_ReturnsDecimal()
    {
        var rows = new List<JsonElement> { ParseElement("{\"amount\": 123.45}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(123.45m, reader.GetDecimal(0));
    }

    [Fact]
    public async Task GetString_ReturnsString()
    {
        var rows = new List<JsonElement> { ParseElement("{\"name\": \"Hello World\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("Hello World", reader.GetString(0));
    }

    [Fact]
    public async Task GetString_WhenColumnIsNull_ReturnsNull()
    {
        // EF Core materializer lambdas call GetString without IsDBNull for non-nullable
        // string properties on optional columns; returning null lets those round-trips work.
        var rows = new List<JsonElement> { ParseElement("{\"name\": null}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Null(reader.GetString(0));
    }

    [Fact]
    public async Task GetDateTime_ReturnsDateTime()
    {
        var rows = new List<JsonElement> { ParseElement("{\"created\": \"2025-01-15T10:30:00Z\"}") };
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
        var rows = new List<JsonElement> { ParseElement($"{{\"id\": \"{guid}\"}}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(guid, reader.GetGuid(0));
    }

    [Fact]
    public async Task IsDBNull_WithNull_ReturnsTrue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"value\": null}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task IsDBNull_WithValue_ReturnsFalse()
    {
        var rows = new List<JsonElement> { ParseElement("{\"value\": 42}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.False(reader.IsDBNull(0));
    }

    [Fact]
    public async Task GetValues_FillsArray()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\", \"active\": true}") };
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
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\", \"active\": true}") };
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
        var rows = new List<JsonElement> { ParseElement("{\"id\": 42}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader[0]);
    }

    [Fact]
    public async Task Indexer_ByName_ReturnsValue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 42}") };
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
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
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
    public async Task HasRows_WithRows_ReturnsTrue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        // After Phase 3: HasRows is false before ReadAsync (no peeking).
        Assert.False(reader.HasRows);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(reader.HasRows);
    }

    [Fact]
    public void HasRows_WithNoRows_ReturnsFalse()
    {
        var reader = CreateReader(new List<JsonElement>());

        Assert.False(reader.HasRows);
    }

    [Fact]
    public async Task HasRows_AfterReadingAllRows_StillReturnsTrue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task HasRows_DoesNotSkipFirstRow()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}")
        };
        var reader = CreateReader(rows);

        // After Phase 3: HasRows returns false before ReadAsync (no peeking).
        Assert.False(reader.HasRows);

        // All rows remain readable.
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(reader.HasRows);
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetSchemaTable_ReturnsSchemaTable()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1, \"name\": \"test\"}") };
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
        var rows = new List<JsonElement> { ParseElement("{\"count\": 42}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("Int64", reader.GetDataTypeName(0));
    }

    [Fact]
    public async Task GetFieldType_ReturnsType()
    {
        var rows = new List<JsonElement> { ParseElement("{\"count\": 42}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(typeof(long), reader.GetFieldType(0));
    }

    [Fact]
    public async Task GetFieldValue_Generic_ReturnsTypedValue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"count\": 42}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetFieldValue<long>(0));
    }

    [Fact]
    public async Task GetChar_ReturnsFirstChar()
    {
        var rows = new List<JsonElement> { ParseElement("{\"letter\": \"A\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal('A', reader.GetChar(0));
    }

    [Fact]
    public async Task GetByte_ReturnsByte()
    {
        var rows = new List<JsonElement> { ParseElement("{\"value\": 255}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((byte)255, reader.GetByte(0));
    }

    [Fact]
    public async Task GetInt16_ReturnsShort()
    {
        var rows = new List<JsonElement> { ParseElement("{\"value\": 32767}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((short)32767, reader.GetInt16(0));
    }

    [Fact]
    public async Task GetFloat_ReturnsFloat()
    {
        var rows = new List<JsonElement> { ParseElement("{\"value\": 3.14}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(3.14f, reader.GetFloat(0), 2);
    }

    [Fact]
    public async Task Dispose_ClosesReader()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        reader.Dispose();

        Assert.True(reader.IsClosed);
    }

    [Fact]
    public async Task DisposeAsync_ClosesReader()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
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

    [Fact]
    public async Task FieldCount_AfterFirstRead_ReturnsCorrectCount()
    {
        // After Phase 3: without column names, FieldCount requires a current row.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}")
        };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1, reader.FieldCount);
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetName_AfterFirstRead_ReturnsFieldName()
    {
        // After Phase 3: without column names, GetName resolves positionally from the current row.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"name\": \"first\"}"),
            ParseElement("{\"name\": \"second\"}")
        };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("name", reader.GetName(0));
        Assert.Equal("first", reader.GetString(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("second", reader.GetString(0));
    }

    [Fact]
    public async Task GetOrdinal_AfterFirstRead_ReturnsCorrectOrdinal()
    {
        // After Phase 3: without column names, GetOrdinal resolves positionally from the current row.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"value\": 100}")
        };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(0, reader.GetOrdinal("value"));
        Assert.Equal(100L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetSchemaTable_AfterFirstRead_ReflectsColumnNames()
    {
        // After Phase 3: without column names, GetSchemaTable derives schema from the current row.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"col1\": 1, \"col2\": 2}")
        };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        var schema = reader.GetSchemaTable();
        Assert.Equal(2, schema.Rows.Count);

        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    #region Value-Type Row Tracking Tests

    [Fact]
    public void GetValue_BeforeAnyRead_ThrowsInvalidOperationException()
    {
        // JsonElement is a value type — this tests that we track row availability
        // with a boolean flag, not by checking _currentRow == null
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        var ex = Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Contains("No current row", ex.Message);
    }

    [Fact]
    public async Task GetValue_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        // After exhausting all rows, GetValue should throw even though
        // _currentRow (being a value type) is not null but default(JsonElement)
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetValue(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        var ex = Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Contains("No current row", ex.Message);
    }

    [Fact]
    public async Task GetBoolean_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"flag\": true}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(reader.GetBoolean(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public async Task GetString_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"name\": \"test\"}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("test", reader.GetString(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Fact]
    public async Task GetInt64_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"num\": 42}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(42L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetInt64(0));
    }

    [Fact]
    public async Task IsDBNull_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": null}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(reader.IsDBNull(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.IsDBNull(0));
    }

    [Fact]
    public async Task GetValues_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"a\": 1, \"b\": 2}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        var values = new object[2];
        Assert.Equal(2, reader.GetValues(values));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetValues(values));
    }

    [Fact]
    public async Task Indexer_ByOrdinal_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"x\": 1}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader[0]);

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => _ = reader[0]);
    }

    [Fact]
    public async Task Indexer_ByName_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"x\": 1}") };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader["x"]);

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => _ = reader["x"]);
    }

    [Fact]
    public async Task ValueTypeRow_MultipleReadCycles_TracksStateCorrectly()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}"),
            ParseElement("{\"id\": 3}")
        };
        var reader = CreateReader(rows);

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(3L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task ReadAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(cts.Token));
    }

    [Fact]
    public async Task ReadAsync_TokenCancelledDuringIteration_ThrowsOnNextRead()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}")
        };
        var reader = CreateReader(rows);

        using var cts = new CancellationTokenSource();

        Assert.True(await reader.ReadAsync(cts.Token));
        Assert.Equal(1L, reader.GetInt64(0));

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(cts.Token));
    }

    [Fact]
    public async Task ReadAsync_CancellationTokenCapturedOnFirstRead()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}")
        };
        var reader = CreateReader(rows);

        using var cts = new CancellationTokenSource();

        Assert.True(await reader.ReadAsync(cts.Token));
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    #endregion

    #region Overflow Tests

    [Fact]
    public async Task GetByte_WithValueGreaterThan255_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 256}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetByte(0));
    }

    [Fact]
    public async Task GetByte_WithNegativeValue_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": -1}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetByte(0));
    }

    [Fact]
    public async Task GetByte_WithValidValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 255}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((byte)255, reader.GetByte(0));
    }

    [Fact]
    public async Task GetInt16_WithValueGreaterThanMaxShort_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 32768}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt16(0));
    }

    [Fact]
    public async Task GetInt16_WithValueLessThanMinShort_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": -32769}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt16(0));
    }

    [Fact]
    public async Task GetInt16_WithValidValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 32767}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((short)32767, reader.GetInt16(0));
    }

    [Fact]
    public async Task GetInt32_WithValueGreaterThanMaxInt_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 2147483648}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt32_WithValueLessThanMinInt_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": -2147483649}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt32_WithValidValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 2147483647}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(2147483647, reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt32_WhenJsonNumberHasFractionalRepresentation_ReturnsIntValue()
    {
        // System.Text.Json TryGetInt64 returns false for numbers stored with a decimal
        // point (e.g. 2.0), so ConvertJsonElement falls back to GetDouble() and returns
        // a double.  GetInt32 must convert that double via Convert.ToInt32 rather than
        // throwing, consistent with every other numeric getter's fallback arm.
        var rows = new List<JsonElement> { ParseElement("{\"count\": 2.0}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(2, reader.GetInt32(0));
    }

    [Fact]
    public async Task GetFloat_WithValueExceedingFloatRange_ReturnsInfinity()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 3.5E+38}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetFloat(0);
        Assert.True(float.IsInfinity(result));
    }

    [Fact]
    public async Task GetDecimal_WithValueExceedingDecimalRange_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"val\": 1.8E+308}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetDecimal(0));
    }

    #endregion

    #region GetBytes/GetChars Bounds Validation Tests

    [Fact]
    public async Task GetBytes_WithNegativeDataOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, -1, buffer, 0, 5));
    }

    [Fact]
    public async Task GetBytes_WithNegativeBufferOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, -1, 5));
    }

    [Fact]
    public async Task GetBytes_WithBufferOffsetExceedingBufferLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, 11, 5));
    }

    [Fact]
    public async Task GetBytes_WithBufferOffsetAtEndAndZeroLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        var result = reader.GetBytes(0, 0, buffer, 10, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetBytes_WithNegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, 0, -1));
    }

    [Fact]
    public async Task GetBytes_WithDataOffsetBeyondSourceLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        var result = reader.GetBytes(0, 100, buffer, 0, 5);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetBytes_WithNullBuffer_ReturnsTotalLength()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") }; // "Hello" = 5 bytes
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetBytes(0, 0, null, 0, 0);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task GetBytes_WithValidParameters_CopiesCorrectData()
    {
        var rows = new List<JsonElement> { ParseElement("{\"data\": \"SGVsbG8=\"}") }; // "Hello"
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        var result = reader.GetBytes(0, 1, buffer, 2, 3);

        Assert.Equal(3, result);
        Assert.Equal((byte)'e', buffer[2]);
        Assert.Equal((byte)'l', buffer[3]);
        Assert.Equal((byte)'l', buffer[4]);
    }

    [Fact]
    public async Task GetChars_WithNegativeDataOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, -1, buffer, 0, 5));
    }

    [Fact]
    public async Task GetChars_WithNegativeBufferOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 0, buffer, -1, 5));
    }

    [Fact]
    public async Task GetChars_WithBufferOffsetExceedingBufferLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 0, buffer, 11, 5));
    }

    [Fact]
    public async Task GetChars_WithBufferOffsetAtEndAndZeroLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        var result = reader.GetChars(0, 0, buffer, 10, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetChars_WithNegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 0, buffer, 0, -1));
    }

    [Fact]
    public async Task GetChars_WithDataOffsetBeyondSourceLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        var result = reader.GetChars(0, 100, buffer, 0, 5);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetChars_WithNullBuffer_ReturnsTotalLength()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetChars(0, 0, null, 0, 0);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task GetChars_WithValidParameters_CopiesCorrectData()
    {
        var rows = new List<JsonElement> { ParseElement("{\"text\": \"Hello\"}") };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        var result = reader.GetChars(0, 1, buffer, 2, 3);

        Assert.Equal(3, result);
        Assert.Equal('e', buffer[2]);
        Assert.Equal('l', buffer[3]);
        Assert.Equal('l', buffer[4]);
    }

    #endregion

    #region Phase 3 — projection-alias column-name mapping bugs

    [Fact]
    public async Task GetValue_NullColumnNameSlot_FallsBackToPositionalAccess()
    {
        // Bug: when _columnNames[ordinal] is null the contract says "use positional
        // access", but the current implementation returns DBNull.Value instead.
        // Row fields in document order: id=1 at position 0, name="test" at position 1.
        // _columnNames has null at slot 0 (no alias → positional) and "name" at slot 1.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"test\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        // Slot 0 is null: should return the value at positional ordinal 0 (id = 1).
        Assert.Equal(1L, reader.GetValue(0));
        // Slot 1 is "name": name-based lookup should return "test".
        Assert.Equal("test", reader.GetValue(1));
    }

    [Fact]
    public async Task GetValue_ScalarRowWithNonEmptyColumnAlias_ReturnsScalarValue()
    {
        // Bug: SELECT RAW COUNT(*) produces a bare numeric JsonElement whose schema
        // has a single synthetic "" field.  When _columnNames is set with a non-empty
        // alias (e.g. "c"), _fieldOrdinals.TryGetValue("c") fails because only "" is
        // registered, so GetValue returns DBNull.Value instead of the scalar number.
        var rows = new List<JsonElement> { ParseElement("42") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "c" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_MissingDocumentField_ReturnsDBNull()
    {
        // Confirm the one case where DBNull.Value IS correct: the alias is present in
        // _columnNames but the field is absent from the N1QL response (MISSING field).
        var rows = new List<JsonElement>
        {
            ParseElement("{\"name\": \"test\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id", "name" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, reader.GetValue(0)); // "id" not in response → MISSING
        Assert.Equal("test", reader.GetValue(1));
    }

    #endregion

    #region Phase 3 — GetOrdinal/GetName/FieldCount/schema with projection aliases

    [Fact]
    public async Task GetOrdinal_WithColumnNames_ReturnsProjectionOrdinal()
    {
        // JSON response order: id=0, name=1.  Projection maps "name" to slot 0, "id" to slot 1.
        // GetOrdinal must return the projection slot, not the JSON response position.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "name", "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("name"));
        Assert.Equal(1, reader.GetOrdinal("id"));
    }

    [Fact]
    public async Task GetOrdinal_WithColumnNames_IsCaseInsensitive()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "name", "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("NAME"));
        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Equal(1, reader.GetOrdinal("ID"));
    }

    [Fact]
    public async Task GetOrdinal_WithColumnNames_UnknownNameThrows()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nonexistent"));
    }

    [Fact]
    public async Task IndexerByName_WithColumnNames_ReturnsCorrectValue()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 99, \"name\": \"bob\"}")
        };
        // Projection maps "name" → slot 0, "id" → slot 1 (reversed from JSON order).
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "name", "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("bob", reader["name"]);
        Assert.Equal(99L, reader["id"]);
    }

    [Fact]
    public async Task GetName_WithColumnNames_ReturnsProjectionAlias()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "name", "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("name", reader.GetName(0));
        Assert.Equal("id", reader.GetName(1));
    }

    [Fact]
    public async Task GetName_WithColumnNames_NullSlotFallsBackToJsonFieldName()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        // Slot 0 is null (positional), slot 1 has an explicit alias.
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("id", reader.GetName(0));   // positional fallback to JSON field name
        Assert.Equal("name", reader.GetName(1));
    }

    [Fact]
    public void GetName_NullSlot_NoCurrentRow_ThrowsInvalidOperationException()
    {
        // Ordinal 0 is a null slot (in-range), but no ReadAsync has been called.
        // Must throw InvalidOperationException (no current row), not IndexOutOfRangeException.
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null });

        Assert.Throws<InvalidOperationException>(() => reader.GetName(0));
    }

    [Fact]
    public void FieldCount_WithColumnNames_ReturnsProjectionLength()
    {
        // JSON has 3 fields, but projection only exposes 2.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"a\": 1, \"b\": 2, \"c\": 3}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b" });

        Assert.Equal(2, reader.FieldCount);
    }

    [Fact]
    public async Task GetSchemaTable_WithColumnNames_ReflectsProjectionAliases()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "name", "id" });
        await reader.ReadAsync(CancellationToken.None);

        var schema = reader.GetSchemaTable();

        Assert.Equal(2, schema.Rows.Count);
        Assert.Equal("name", schema.Rows[0]["ColumnName"]);
        Assert.Equal(0, schema.Rows[0]["ColumnOrdinal"]);
        Assert.Equal("id", schema.Rows[1]["ColumnName"]);
        Assert.Equal(1, schema.Rows[1]["ColumnOrdinal"]);
    }

    [Fact]
    public async Task GetOrdinal_WithColumnNames_DoesNotRequireSchemaDiscovery()
    {
        // GetOrdinal with _columnNames must resolve without requiring a prior ReadAsync call.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });

        // No Read() call — should still return the projection ordinal.
        Assert.Equal(0, reader.GetOrdinal("id"));

        // First Read() must still return the first row (no row was consumed).
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));
    }

    #endregion

    #region Phase 3 — GetOrdinal/GetName inverse for null slots and GetValue MISSING positional

    [Fact]
    public async Task GetOrdinal_NullSlot_RoundTripsWithGetName()
    {
        // GetName(0) returns the JSON field name "id" for a null slot.
        // GetOrdinal("id") must return 0 so the pair are inverses.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        var name = reader.GetName(0);          // "id" via positional fallback
        Assert.Equal(0, reader.GetOrdinal(name));
    }

    [Fact]
    public async Task GetOrdinal_NullSlot_CaseInsensitive()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"MyField\": 99}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("myfield"));
        Assert.Equal(0, reader.GetOrdinal("MYFIELD"));
        Assert.Equal(0, reader.GetOrdinal("MyField"));
    }

    [Fact]
    public async Task GetOrdinal_NullSlot_NonMatchingNameStillThrows()
    {
        // The constrained fallback only accepts names that map to a null-slot position.
        // An arbitrary unknown name must still throw.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nonexistent"));
    }

    [Fact]
    public void GetOrdinal_NullSlot_NoCurrentRow_ThrowsInvalidOperationException()
    {
        // Null-slot resolution requires a current row. Before ReadAsync is called the
        // exception must be InvalidOperationException, not IndexOutOfRangeException.
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null });

        Assert.Throws<InvalidOperationException>(() => reader.GetOrdinal("id"));
    }

    [Fact]
    public async Task GetOrdinal_NullSlot_AliasNameInNonNullSlotIsNotResolvedViaFallback()
    {
        // "name" is already a non-null alias at slot 1.  If someone asks for ordinal
        // of a JSON field that happens to share that string but lives at slot 0 (null),
        // the alias lookup wins — it returns 1, not 0.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"name\": \"positional\", \"title\": \"alias\"}")
        };
        // Slot 0 is null (positional → "name"), slot 1 has alias "name" (mapped to "title").
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(1, reader.GetOrdinal("name")); // alias at slot 1 wins
    }

    [Fact]
    public async Task GetValue_NullSlot_PositionalMissingFieldReturnsDBNull()
    {
        // JSON response has only 1 field; projection has 2 null slots.
        // Slot 0 resolves positionally to "id" — present.
        // Slot 1 is a null slot beyond the JSON field count — must be DBNull, not throw.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 42}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, null });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetValue(0));
        Assert.Equal(DBNull.Value, reader.GetValue(1));
    }

    [Fact]
    public async Task GetValue_NoColumnNames_OutOfRangeOrdinalThrows()
    {
        // Without _columnNames, an out-of-range ordinal is programmer error and must throw.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 7}")
        };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(7L, reader.GetValue(0));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(99));
    }

    #endregion

    #region Phase 3 — GetSchemaTable null-slot, duplicate aliases, GetValues, and edge cases

    [Fact]
    public async Task GetSchemaTable_WithNullSlot_StoresDBNullForColumnName()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        var schema = reader.GetSchemaTable();

        Assert.Equal(2, schema.Rows.Count);
        Assert.Equal(DBNull.Value, schema.Rows[0]["ColumnName"]);
        Assert.Equal(0, schema.Rows[0]["ColumnOrdinal"]);
        Assert.Equal("name", schema.Rows[1]["ColumnName"]);
        Assert.Equal(1, schema.Rows[1]["ColumnOrdinal"]);
    }

    [Fact]
    public async Task GetOrdinal_WithDuplicateColumnNames_FirstWins()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "dup", "dup" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("dup"));
    }

    [Fact]
    public async Task GetValues_WithColumnNames_UsesProjectionCount()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"a\": 1, \"b\": 2, \"c\": 3}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b" });
        await reader.ReadAsync(CancellationToken.None);

        var values = new object[5];
        var count = reader.GetValues(values);

        Assert.Equal(2, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal(2L, values[1]);
        Assert.Null(values[2]);
    }

    [Fact]
    public async Task GetValues_NullArgument_ThrowsArgumentNullException()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<ArgumentNullException>(() => reader.GetValues(null!));
    }

    [Fact]
    public async Task GetName_WithColumnNames_OutOfRange_ThrowsIndexOutOfRangeException()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetName(1));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetName(-1));
    }

    [Fact]
    public async Task GetOrdinal_WithAllNullColumnNames_ResolvesJsonFieldNamesViaFallback()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1, \"name\": \"alice\"}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, null });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("id"));
        Assert.Equal(1, reader.GetOrdinal("name"));

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("anything"));
    }

    [Fact]
    public void FieldCount_WithEmptyColumnNames_ReturnsZero()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { });

        Assert.Equal(0, reader.FieldCount);
    }

    [Fact]
    public async Task GetValues_WithColumnNamesAndSmallerArray_UsesMinOfArrayAndProjection()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"a\": 10, \"b\": 20, \"c\": 30}")
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b", "c" });
        await reader.ReadAsync(CancellationToken.None);

        var values = new object[2];
        var count = reader.GetValues(values);

        Assert.Equal(2, count);
        Assert.Equal(10L, values[0]);
        Assert.Equal(20L, values[1]);
    }

    #endregion

    #region Phase 3 — scalar SELECT RAW and shaper-compatible access

    [Fact]
    public async Task ScalarRaw_NumberRow_FieldCountIsOne()
    {
        var rows = new List<JsonElement> { ParseElement("42") };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(1, reader.FieldCount);
    }

    [Fact]
    public async Task ScalarRaw_NumberRow_GetValueReturnsNumber()
    {
        var rows = new List<JsonElement> { ParseElement("42") };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        var value = reader.GetValue(0);
        Assert.Equal(42L, value);
    }

    [Fact]
    public async Task ScalarRaw_NumberRow_GetOrdinalWithAnyNameReturnsZero()
    {
        // No-column-names path: scalar SELECT RAW maps any name to ordinal 0.
        var rows = new List<JsonElement> { ParseElement("5") };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(0, reader.GetOrdinal("c0"));
        Assert.Equal(0, reader.GetOrdinal("any_alias"));
    }

    [Fact]
    public async Task ScalarRaw_StringRow_GetValueReturnsString()
    {
        var rows = new List<JsonElement> { ParseElement("\"hello\"") };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal("hello", reader.GetValue(0));
    }

    [Fact]
    public async Task ObjectRow_SingleField_GetOrdinalUnknownNameStillThrows()
    {
        // An object row with one named field should NOT fall back to ordinal 0 for unknown names.
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nonexistent"));
    }

    [Fact]
    public async Task GetValue_ObjectField_ReturnsRawJsonElement()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"nested\": {\"x\": 1, \"y\": 2}}")
        };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);

        var value = reader.GetValue(0);

        var je = Assert.IsType<JsonElement>(value);
        Assert.Equal(JsonValueKind.Object, je.ValueKind);
        Assert.Equal(1, je.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task GetValue_ArrayField_ReturnsRawJsonElement()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"tags\": [\"a\", \"b\", \"c\"]}")
        };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);

        var value = reader.GetValue(0);

        var je = Assert.IsType<JsonElement>(value);
        Assert.Equal(JsonValueKind.Array, je.ValueKind);
        Assert.Equal(3, je.GetArrayLength());
    }

    #endregion

    #region PrimeAsync — HasRows and buffered-first-row via PrimeAsync

    [Fact]
    public async Task PrimeAsync_WithRows_HasRowsIsTrueBeforeReadAsync()
    {
        // Simulates the ExecuteReaderAsync path: PrimeAsync is called after construction,
        // and HasRows must return true before any ReadAsync call.
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        await reader.PrimeAsync(CancellationToken.None);

        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task PrimeAsync_WithNoRows_HasRowsIsFalseBeforeReadAsync()
    {
        var reader = CreateReader(new List<JsonElement>());

        await reader.PrimeAsync(CancellationToken.None);

        Assert.False(reader.HasRows);
    }

    [Fact]
    public async Task PrimeAsync_FirstReadAsyncReturnsBufferedRow()
    {
        // After PrimeAsync, the first ReadAsync must return the buffered first row
        // without skipping it (no row consumed by the prime peek is lost).
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}")
        };
        var reader = CreateReader(rows);

        await reader.PrimeAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PrimeAsync_WithNoRows_ReadAsyncReturnsFalse()
    {
        var reader = CreateReader(new List<JsonElement>());

        await reader.PrimeAsync(CancellationToken.None);

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PrimeAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var rows = new List<JsonElement> { ParseElement("{\"id\": 1}") };
        var reader = CreateReader(rows);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // PrimeAsync calls EnsureEnumerator then MoveNextAsync; the enumerator
        // created via GetAsyncEnumerator(cancelledToken) should propagate cancellation.
        // (The exact throw point is implementation-defined, but cancellation must surface.)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.PrimeAsync(cts.Token));
    }

    [Fact]
    public async Task PrimeAsync_FieldAccessAfterBufferedRead_WorksCorrectly()
    {
        // After PrimeAsync + ReadAsync, all field accessors must see the buffered row.
        var rows = new List<JsonElement> { ParseElement("{\"id\": 99, \"name\": \"alice\"}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id", "name" });

        await reader.PrimeAsync(CancellationToken.None);
        Assert.True(reader.HasRows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(99L, reader.GetInt64(0));
        Assert.Equal("alice", reader.GetString(1));
    }

    [Fact]
    public async Task PrimeAsync_CalledTwice_IsIdempotent()
    {
        // A second PrimeAsync call must not advance the enumerator again or overwrite
        // the buffered first row.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2}")
        };
        var reader = CreateReader(rows);

        await reader.PrimeAsync(CancellationToken.None);
        await reader.PrimeAsync(CancellationToken.None); // second call must be a no-op

        Assert.True(reader.HasRows);

        // Both rows must still be readable — the first row was not skipped.
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PrimeAsync_CalledAfterReadAsync_IsIdempotent()
    {
        // PrimeAsync after ReadAsync has already advanced the reader must not
        // consume another row or alter HasRows.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 10}"),
            ParseElement("{\"id\": 20}")
        };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(10L, reader.GetInt64(0));

        await reader.PrimeAsync(CancellationToken.None); // must be a no-op

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(20L, reader.GetInt64(0));
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    #endregion

    #region Row-type validation — NotSupportedException for non-JsonElement rows

    [Fact]
    public async Task ReadAsync_NonJsonElementRow_ThrowsNotSupportedException()
    {
        var mockQueryResult = new Mock<IQueryResult<object>>();
        mockQueryResult.Setup(q => q.Rows).Returns(new[] { (object)"not-a-json-element" }.ToAsyncEnumerable());
        var reader = new CouchbaseDbDataReader<object>(mockQueryResult.Object, connection: null,
            System.Data.CommandBehavior.Default, CancellationToken.None);

        await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PrimeAsync_NonJsonElementRow_ThrowsNotSupportedException()
    {
        var mockQueryResult = new Mock<IQueryResult<object>>();
        mockQueryResult.Setup(q => q.Rows).Returns(new[] { (object)"not-a-json-element" }.ToAsyncEnumerable());
        var reader = new CouchbaseDbDataReader<object>(mockQueryResult.Object, connection: null,
            System.Data.CommandBehavior.Default, CancellationToken.None);

        await Assert.ThrowsAsync<NotSupportedException>(() => reader.PrimeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_NullRow_DoesNotThrow()
    {
        // null is allowed (e.g. SELECT RAW null) — only non-null non-JsonElement throws.
        var mockQueryResult = new Mock<IQueryResult<object>>();
        mockQueryResult.Setup(q => q.Rows).Returns(new object?[] { null }.ToAsyncEnumerable<object?>());
        var reader = new CouchbaseDbDataReader<object?>(mockQueryResult.Object!, connection: null,
            System.Data.CommandBehavior.Default, CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
    }

    #endregion

    #region Phase 2 — TryGetPropertyCI and GetOrdinal null-slot guards

    // GetValue — case-insensitive property lookup via TryGetPropertyCI

    [Fact]
    public async Task GetValue_WithColumnNames_CaseInsensitiveAlias_ReturnsValue()
    {
        // JSON has camelCase key; projection alias uses PascalCase — must still resolve.
        var rows = new List<JsonElement> { ParseElement("{\"blogId\": 42}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "BlogId" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(42L, reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_WithColumnNames_MissingJsonField_ReturnsDBNull()
    {
        // Alias present in projection but the JSON row does not contain the property.
        var rows = new List<JsonElement> { ParseElement("{\"other\": 1}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "missing" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(DBNull.Value, reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_WithColumnNames_MultipleAliases_ResolvesEachIndependently()
    {
        // Each alias resolves via TryGetPropertyCI to its value.
        var rows = new List<JsonElement> { ParseElement("{\"name\": \"alice\", \"age\": 30}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "age", "name" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(30L, reader.GetValue(0));
        Assert.Equal("alice", reader.GetValue(1));
    }

    // GetValue — canonical-name path via TryGetPropertyCI

    [Fact]
    public async Task GetValue_WithColumnNames_HasRows_CaseInsensitiveAliasResolves()
    {
        // Accessing HasRows before ReadAsync no longer triggers schema discovery.
        // GetValue must still resolve the alias correctly via TryGetPropertyCI.
        var rows = new List<JsonElement> { ParseElement("{\"blogId\": 99}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "BlogId" });

        _ = reader.HasRows; // returns false before ReadAsync; no side effects
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(99L, reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_WithColumnNames_MultipleRows_AliasUsedForEveryRow()
    {
        // TryGetPropertyCI resolves the alias for every row.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"blogId\": 1}"),
            ParseElement("{\"blogId\": 2}"),
            ParseElement("{\"blogId\": 3}"),
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "BlogId" });

        var results = new List<long>();
        while (await reader.ReadAsync(CancellationToken.None))
            results.Add((long)reader.GetValue(0));

        Assert.Equal([1L, 2L, 3L], results);
    }

    [Fact]
    public async Task GetValue_WithColumnNames_FieldPresentInFirstRowAbsentInLater_ReturnsDBNull()
    {
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 10}"),
            ParseElement("{\"other\": 99}"),
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });

        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(10L, reader.GetValue(0));

        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(DBNull.Value, reader.GetValue(0));
    }

    [Fact]
    public async Task GetValue_WithColumnNames_AliasNotInFirstRow_FallsBackToTryGetPropertyCI()
    {
        // The second row introduces a field absent from the first row.
        // TryGetPropertyCI finds it case-insensitively on both rows.
        var rows = new List<JsonElement>
        {
            ParseElement("{\"id\": 1}"),
            ParseElement("{\"id\": 2, \"Extra\": 42}"),
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id", "extra" });

        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(1L, reader.GetValue(0));
        Assert.Equal(DBNull.Value, reader.GetValue(1)); // "extra" absent from first row

        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(2L, reader.GetValue(0));
        // TryGetPropertyCI finds "Extra" case-insensitively.
        Assert.Equal(42L, reader.GetValue(1));
    }

    // GetOrdinal null-slot fallback — bounds check and null-slot guard

    [Fact]
    public async Task GetOrdinal_NullSlotFallback_FieldBeyondProjectionWidth_Throws()
    {
        // JSON row has 3 fields; projection only covers 2 columns.
        // GetOrdinal for the 3rd field name must throw — its ordinal (2) is >= FieldCount (2).
        var rows = new List<JsonElement> { ParseElement("{\"a\": 1, \"b\": 2, \"c\": 3}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("c"));
    }

    [Fact]
    public async Task GetOrdinal_NullSlotFallback_NameMapsToNonNullSlot_Throws()
    {
        // JSON fields "a" and "b" map to ordinals 0 and 1.
        // Both slots have non-null aliases, so neither "a" nor "b" should resolve
        // via the null-slot fallback — they are not in _projectionOrdinals (aliases differ),
        // and the null-slot guard must reject them.
        var rows = new List<JsonElement> { ParseElement("{\"a\": 1, \"b\": 2}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "ALIAS_A", "ALIAS_B" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("a"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("b"));
    }

    [Fact]
    public async Task GetOrdinal_NullSlotFallback_ValidNullSlot_ReturnsOrdinal()
    {
        // Slot 0 is a null slot; slot 1 has an alias. "a" maps to ordinal 0 (null slot) — valid.
        // "b" maps to ordinal 1, but slot 1 is non-null, so it must be rejected.
        var rows = new List<JsonElement> { ParseElement("{\"a\": 1, \"b\": 2}") };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "ALIAS_B" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(0, reader.GetOrdinal("a"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("b"));
    }

    #endregion

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static CouchbaseDbDataReader<JsonElement> CreateReader(List<JsonElement> rows)
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Success };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.Rows).Returns(rows.ToAsyncEnumerable());

        // Use the raw ADO.NET constructor (no column names) so these tests cover the
        // positional-access path, which resolves field names from the current row.
        return new CouchbaseDbDataReader<JsonElement>(
            mockQueryResult.Object,
            connection: null,
            System.Data.CommandBehavior.Default,
            CancellationToken.None);
    }

    private static CouchbaseDbDataReader<JsonElement> CreateReaderWithColumnNames(
        List<JsonElement> rows,
        string?[]? columnNames)
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Success };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.Rows).Returns(rows.ToAsyncEnumerable());

        return new CouchbaseDbDataReader<JsonElement>(mockQueryResult.Object, columnNames);
    }
}
