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
    public void GetValue_BeforeRead_ThrowsInvalidOperationException()
    {
        var reader = CreateReader(new List<JsonElement>());

        // GetValue requires a current row, which requires calling Read() first
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
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
    public async Task GetString_WhenColumnIsNull_ReturnsNull()
    {
        // EF Core materializer lambdas call GetString without IsDBNull for non-nullable
        // string properties on optional columns; returning null lets those round-trips work.
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"name\": null}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Null(reader.GetString(0));
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
    public void HasRows_WithRows_ReturnsTrue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

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
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        // Read and exhaust all rows
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.False(await reader.ReadAsync(CancellationToken.None));

        // HasRows should still be true (it reflects whether there were rows, not current state)
        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task HasRows_DoesNotSkipFirstRow()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement
        };
        var reader = CreateReader(rows);

        // Check HasRows first
        Assert.True(reader.HasRows);

        // First row should still be readable
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
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

    [Fact]
    public async Task FieldCount_BeforeRead_DoesNotSkipFirstRow()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement
        };
        var reader = CreateReader(rows);

        // Access FieldCount before Read() - this triggers schema discovery
        var fieldCount = reader.FieldCount;
        Assert.Equal(1, fieldCount);

        // First Read() should return the first row, not skip it
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        // Second Read() should return the second row
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        // No more rows
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetName_BeforeRead_DoesNotSkipFirstRow()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"name\": \"first\"}").RootElement,
            JsonDocument.Parse("{\"name\": \"second\"}").RootElement
        };
        var reader = CreateReader(rows);

        // Access GetName before Read() - this triggers schema discovery
        var name = reader.GetName(0);
        Assert.Equal("name", name);

        // First Read() should return the first row
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("first", reader.GetString(0));

        // Second Read() should return the second row
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("second", reader.GetString(0));
    }

    [Fact]
    public async Task GetOrdinal_BeforeRead_DoesNotSkipFirstRow()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"value\": 100}").RootElement
        };
        var reader = CreateReader(rows);

        // Access GetOrdinal before Read()
        var ordinal = reader.GetOrdinal("value");
        Assert.Equal(0, ordinal);

        // First Read() should still return the first (and only) row
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(100L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetSchemaTable_BeforeRead_DoesNotSkipFirstRow()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"col1\": 1, \"col2\": 2}").RootElement
        };
        var reader = CreateReader(rows);

        // Access schema before Read()
        var schema = reader.GetSchemaTable();
        Assert.Equal(2, schema.Rows.Count);

        // First Read() should return the first row
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    #region Value-Type Row Tracking Tests

    [Fact]
    public void GetValue_BeforeAnyRead_ThrowsInvalidOperationException()
    {
        // JsonElement is a value type - this tests that we track row availability
        // with a boolean flag, not by checking _currentRow == null
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        var ex = Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Contains("No current row", ex.Message);
    }

    [Fact]
    public async Task GetValue_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        // After exhausting all rows, GetValue should throw even though
        // _currentRow (being a value type) is not null but default(JsonElement)
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetValue(0)); // This should work

        Assert.False(await reader.ReadAsync(CancellationToken.None)); // No more rows

        var ex = Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Contains("No current row", ex.Message);
    }

    [Fact]
    public async Task GetBoolean_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"flag\": true}").RootElement };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(reader.GetBoolean(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public async Task GetString_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"name\": \"test\"}").RootElement };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("test", reader.GetString(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Fact]
    public async Task GetInt64_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"num\": 42}").RootElement };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(42L, reader.GetInt64(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.GetInt64(0));
    }

    [Fact]
    public async Task IsDBNull_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": null}").RootElement };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.True(reader.IsDBNull(0));

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => reader.IsDBNull(0));
    }

    [Fact]
    public async Task GetValues_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"a\": 1, \"b\": 2}").RootElement };
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
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"x\": 1}").RootElement };
        var reader = CreateReader(rows);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader[0]);

        Assert.False(await reader.ReadAsync(CancellationToken.None));

        Assert.Throws<InvalidOperationException>(() => _ = reader[0]);
    }

    [Fact]
    public async Task Indexer_ByName_AfterReadReturnsFalse_ThrowsInvalidOperationException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"x\": 1}").RootElement };
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
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement,
            JsonDocument.Parse("{\"id\": 3}").RootElement
        };
        var reader = CreateReader(rows);

        // Before first read - no current row
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));

        // Read row 1
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        // Read row 2
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        // Read row 3
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(3L, reader.GetInt64(0));

        // No more rows - should throw again
        Assert.False(await reader.ReadAsync(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task ReadAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
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
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement
        };
        var reader = CreateReader(rows);

        using var cts = new CancellationTokenSource();

        // First read should succeed
        Assert.True(await reader.ReadAsync(cts.Token));
        Assert.Equal(1L, reader.GetInt64(0));

        // Cancel after first read
        cts.Cancel();

        // Next read should throw
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(cts.Token));
    }

    [Fact]
    public async Task ReadAsync_CancellationTokenCapturedOnFirstRead()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement
        };
        var reader = CreateReader(rows);

        using var cts = new CancellationTokenSource();

        // First read captures the token
        Assert.True(await reader.ReadAsync(cts.Token));

        // Subsequent reads with different token still work (enumerator already created)
        Assert.True(await reader.ReadAsync(CancellationToken.None));

        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    #endregion

    #region Overflow Tests

    [Fact]
    public async Task GetByte_WithValueGreaterThan255_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 256}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetByte(0));
    }

    [Fact]
    public async Task GetByte_WithNegativeValue_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": -1}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetByte(0));
    }

    [Fact]
    public async Task GetByte_WithValidValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 255}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((byte)255, reader.GetByte(0));
    }

    [Fact]
    public async Task GetInt16_WithValueGreaterThanMaxShort_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 32768}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt16(0));
    }

    [Fact]
    public async Task GetInt16_WithValueLessThanMinShort_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": -32769}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt16(0));
    }

    [Fact]
    public async Task GetInt16_WithValidValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 32767}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal((short)32767, reader.GetInt16(0));
    }

    [Fact]
    public async Task GetInt32_WithValueGreaterThanMaxInt_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 2147483648}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt32_WithValueLessThanMinInt_ThrowsOverflowException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": -2147483649}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetInt32(0));
    }

    [Fact]
    public async Task GetInt32_WithValidValue_ReturnsValue()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 2147483647}").RootElement };
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
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"count\": 2.0}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(2, reader.GetInt32(0));
    }

    [Fact]
    public async Task GetFloat_WithValueExceedingFloatRange_ReturnsInfinity()
    {
        // Values exceeding float range return infinity rather than throwing
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 3.5E+38}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetFloat(0);
        Assert.True(float.IsInfinity(result));
    }

    [Fact]
    public async Task GetDecimal_WithValueExceedingDecimalRange_ThrowsOverflowException()
    {
        // double.MaxValue exceeds decimal range
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"val\": 1.8E+308}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<OverflowException>(() => reader.GetDecimal(0));
    }

    #endregion

    #region GetBytes/GetChars Bounds Validation Tests

    [Fact]
    public async Task GetBytes_WithNegativeDataOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement }; // "Hello" in base64
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, -1, buffer, 0, 5));
    }

    [Fact]
    public async Task GetBytes_WithNegativeBufferOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, -1, 5));
    }

    [Fact]
    public async Task GetBytes_WithBufferOffsetExceedingBufferLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, 11, 5));
    }

    [Fact]
    public async Task GetBytes_WithBufferOffsetAtEndAndZeroLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        // bufferOffset == buffer.Length is valid when length is 0
        var result = reader.GetBytes(0, 0, buffer, 10, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetBytes_WithNegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, 0, -1));
    }

    [Fact]
    public async Task GetBytes_WithDataOffsetBeyondSourceLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement }; // 5 bytes
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new byte[10];

        var result = reader.GetBytes(0, 100, buffer, 0, 5);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetBytes_WithNullBuffer_ReturnsTotalLength()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement }; // "Hello" = 5 bytes
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetBytes(0, 0, null, 0, 0);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task GetBytes_WithValidParameters_CopiesCorrectData()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"data\": \"SGVsbG8=\"}").RootElement }; // "Hello"
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
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, -1, buffer, 0, 5));
    }

    [Fact]
    public async Task GetChars_WithNegativeBufferOffset_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 0, buffer, -1, 5));
    }

    [Fact]
    public async Task GetChars_WithBufferOffsetExceedingBufferLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 0, buffer, 11, 5));
    }

    [Fact]
    public async Task GetChars_WithBufferOffsetAtEndAndZeroLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        // bufferOffset == buffer.Length is valid when length is 0
        var result = reader.GetChars(0, 0, buffer, 10, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetChars_WithNegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, 0, buffer, 0, -1));
    }

    [Fact]
    public async Task GetChars_WithDataOffsetBeyondSourceLength_ReturnsZero()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement }; // 5 chars
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);
        var buffer = new char[10];

        var result = reader.GetChars(0, 100, buffer, 0, 5);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetChars_WithNullBuffer_ReturnsTotalLength()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
        var reader = CreateReader(rows);

        await reader.ReadAsync(CancellationToken.None);

        var result = reader.GetChars(0, 0, null, 0, 0);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task GetChars_WithValidParameters_CopiesCorrectData()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"text\": \"Hello\"}").RootElement };
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

    #region Initial Cancellation Token Tests

    [Fact]
    public void FieldCount_WithCancelledToken_ThrowsOperationCancelledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var reader = CreateReaderWithCancellationToken(
            new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement },
            cts.Token);

        Assert.Throws<OperationCanceledException>(() => _ = reader.FieldCount);
    }

    [Fact]
    public void HasRows_WithCancelledToken_ThrowsOperationCancelledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var reader = CreateReaderWithCancellationToken(
            new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement },
            cts.Token);

        Assert.Throws<OperationCanceledException>(() => _ = reader.HasRows);
    }

    [Fact]
    public void GetName_WithCancelledToken_ThrowsOperationCancelledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var reader = CreateReaderWithCancellationToken(
            new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement },
            cts.Token);

        Assert.Throws<OperationCanceledException>(() => reader.GetName(0));
    }

    [Fact]
    public void GetOrdinal_WithCancelledToken_ThrowsOperationCancelledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var reader = CreateReaderWithCancellationToken(
            new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement },
            cts.Token);

        Assert.Throws<OperationCanceledException>(() => reader.GetOrdinal("id"));
    }

    [Fact]
    public async Task SchemaDiscovery_WithValidToken_AllowsSubsequentReads()
    {
        // This test verifies that accessing schema properties before ReadAsync
        // uses the initial token and doesn't prevent subsequent reads
        var cts = new CancellationTokenSource();
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement,
            JsonDocument.Parse("{\"id\": 2}").RootElement
        };

        var reader = CreateReaderWithCancellationToken(rows, cts.Token);

        // Access schema before ReadAsync - this triggers enumerator creation with initial token
        Assert.Equal(1, reader.FieldCount);

        // First read should work (buffered row from schema discovery)
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1L, reader.GetInt64(0));

        // Second read should also work
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2L, reader.GetInt64(0));

        // No more rows
        Assert.False(await reader.ReadAsync(CancellationToken.None));
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
            JsonDocument.Parse("{\"id\": 1, \"name\": \"test\"}").RootElement
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
        var rows = new List<JsonElement> { JsonDocument.Parse("42").RootElement };
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
            JsonDocument.Parse("{\"name\": \"test\"}").RootElement
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
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
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
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
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
            JsonDocument.Parse("{\"id\": 1}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nonexistent"));
    }

    [Fact]
    public async Task IndexerByName_WithColumnNames_ReturnsCorrectValue()
    {
        // This is the core contract test: reader["name"] must call GetValue(GetOrdinal("name"))
        // and return the value that the shaper ordinal for "name" maps to — not the wrong one.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 99, \"name\": \"bob\"}").RootElement
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
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
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
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
        };
        // Slot 0 is null (positional), slot 1 has an explicit alias.
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("id", reader.GetName(0));   // positional fallback to JSON field name
        Assert.Equal("name", reader.GetName(1));
    }

    [Fact]
    public void FieldCount_WithColumnNames_ReturnsProjectionLength()
    {
        // JSON has 3 fields, but projection only exposes 2.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"a\": 1, \"b\": 2, \"c\": 3}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b" });

        Assert.Equal(2, reader.FieldCount);
    }

    [Fact]
    public async Task GetSchemaTable_WithColumnNames_ReflectsProjectionAliases()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
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
        // GetOrdinal with _columnNames must resolve without calling EnsureFieldInfo,
        // so it works even before Read() and without triggering a row buffer.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement
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
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
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
            JsonDocument.Parse("{\"MyField\": 99}").RootElement
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
            JsonDocument.Parse("{\"id\": 1}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nonexistent"));
    }

    [Fact]
    public async Task GetOrdinal_NullSlot_AliasNameInNonNullSlotIsNotResolvedViaFallback()
    {
        // "name" is already a non-null alias at slot 1.  If someone asks for ordinal
        // of a JSON field that happens to share that string but lives at slot 0 (null),
        // the alias lookup wins — it returns 1, not 0.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"name\": \"positional\", \"title\": \"alias\"}").RootElement
        };
        // Slot 0 is null (positional → "name"), slot 1 has alias "name" (mapped to "title").
        // This is a degenerate case; alias wins.
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(1, reader.GetOrdinal("name")); // alias at slot 1 wins
    }

    [Fact]
    public async Task GetValue_NullSlot_PositionalMissingFieldReturnsDBNull()
    {
        // JSON response has only 1 field; projection has 2 null slots.
        // Slot 0 resolves positionally to "id" — present.
        // Slot 1 is a null slot beyond _fieldNames.Count — must be DBNull, not throw.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 42}").RootElement
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
        // (DBNull is only returned for null-slot positions beyond the JSON field count.)
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 7}").RootElement
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
        // When _columnNames has a null slot, GetSchemaTable stores DBNull.Value for ColumnName
        // (because there is no alias for that ordinal). The DataTable accepts DBNull.Value for
        // a typeof(string) column (stored as object internally).
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, "name" });
        await reader.ReadAsync(CancellationToken.None);

        var schema = reader.GetSchemaTable();

        Assert.Equal(2, schema.Rows.Count);
        // Null slot → ColumnName is DBNull.Value, not a string
        Assert.Equal(DBNull.Value, schema.Rows[0]["ColumnName"]);
        Assert.Equal(0, schema.Rows[0]["ColumnOrdinal"]);
        // Non-null slot → ColumnName is the alias
        Assert.Equal("name", schema.Rows[1]["ColumnName"]);
        Assert.Equal(1, schema.Rows[1]["ColumnOrdinal"]);
    }

    [Fact]
    public async Task GetOrdinal_WithDuplicateColumnNames_FirstWins()
    {
        // When two slots share the same alias, TryAdd keeps the first.
        // GetOrdinal("dup") must return the first slot's ordinal (0), not the second (1).
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "dup", "dup" });
        await reader.ReadAsync(CancellationToken.None);

        // First-wins: ordinal 0 is returned for "dup" even though slot 1 also has it.
        Assert.Equal(0, reader.GetOrdinal("dup"));
    }

    [Fact]
    public async Task GetValues_WithColumnNames_UsesProjectionCount()
    {
        // When _columnNames is set and has fewer slots than the JSON fields, GetValues
        // must iterate over projection count (2), not JSON field count (3).
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"a\": 1, \"b\": 2, \"c\": 3}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b" });
        await reader.ReadAsync(CancellationToken.None);

        var values = new object[5];
        var count = reader.GetValues(values);

        // Only 2 slots in projection — count must be min(5, 2) = 2
        Assert.Equal(2, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal(2L, values[1]);
        // Remaining entries in values are untouched (default null)
        Assert.Null(values[2]);
    }

    [Fact]
    public async Task GetValues_NullArgument_ThrowsArgumentNullException()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });
        await reader.ReadAsync(CancellationToken.None);

        Assert.Throws<ArgumentNullException>(() => reader.GetValues(null!));
    }

    [Fact]
    public async Task GetName_WithColumnNames_OutOfRange_ThrowsIndexOutOfRangeException()
    {
        // The (uint)ordinal >= (uint)_columnNames.Length guard at line 369 must fire.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "id" });
        await reader.ReadAsync(CancellationToken.None);

        // Ordinal 1 is out of range for a 1-element projection.
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetName(1));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetName(-1));
    }

    [Fact]
    public async Task GetOrdinal_WithAllNullColumnNames_ResolvesJsonFieldNamesViaFallback()
    {
        // _projectionOrdinals is empty (all slots null), but the constrained fallback
        // resolves JSON field names that map to null-slot positions.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1, \"name\": \"alice\"}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { null, null });
        await reader.ReadAsync(CancellationToken.None);

        // Both slots are null — JSON field names resolve via the constrained fallback.
        Assert.Equal(0, reader.GetOrdinal("id"));
        Assert.Equal(1, reader.GetOrdinal("name"));

        // A name that doesn't exist in the JSON response still throws.
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("anything"));
    }

    [Fact]
    public void FieldCount_WithEmptyColumnNames_ReturnsZero()
    {
        // Empty _columnNames array → FieldCount must be 0 without triggering schema discovery.
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"id\": 1}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { });

        Assert.Equal(0, reader.FieldCount);
    }

    [Fact]
    public async Task GetValues_WithColumnNamesAndSmallerArray_UsesMinOfArrayAndProjection()
    {
        // Array is smaller than projection — must return min(array.Length, _columnNames.Length).
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"a\": 10, \"b\": 20, \"c\": 30}").RootElement
        };
        var reader = CreateReaderWithColumnNames(rows, new string?[] { "a", "b", "c" });
        await reader.ReadAsync(CancellationToken.None);

        var values = new object[2]; // smaller than projection (3)
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
        var rows = new List<JsonElement> { JsonDocument.Parse("42").RootElement };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(1, reader.FieldCount);
    }

    [Fact]
    public async Task ScalarRaw_NumberRow_GetValueReturnsNumber()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("42").RootElement };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        var value = reader.GetValue(0);
        Assert.Equal(42L, value);
    }

    [Fact]
    public async Task ScalarRaw_NumberRow_GetOrdinalWithAnyNameReturnsZero()
    {
        // The EF Core shaper may request any alias name for scalar projections;
        // a SELECT RAW result maps any name to ordinal 0.
        var rows = new List<JsonElement> { JsonDocument.Parse("5").RootElement };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(0, reader.GetOrdinal("c0"));
        Assert.Equal(0, reader.GetOrdinal("any_alias"));
    }

    [Fact]
    public async Task ScalarRaw_StringRow_GetValueReturnsString()
    {
        var rows = new List<JsonElement> { JsonDocument.Parse("\"hello\"").RootElement };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Equal("hello", reader.GetValue(0));
    }

    [Fact]
    public async Task ObjectRow_SingleField_GetOrdinalUnknownNameStillThrows()
    {
        // An object row with one named field should NOT fall back to ordinal 0 for unknown names.
        var rows = new List<JsonElement> { JsonDocument.Parse("{\"id\": 1}").RootElement };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nonexistent"));
    }

    [Fact]
    public async Task GetValue_ObjectField_ReturnsRawJsonElement()
    {
        var rows = new List<JsonElement>
        {
            JsonDocument.Parse("{\"nested\": {\"x\": 1, \"y\": 2}}").RootElement
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
            JsonDocument.Parse("{\"tags\": [\"a\", \"b\", \"c\"]}").RootElement
        };
        var reader = CreateReader(rows);
        await reader.ReadAsync(CancellationToken.None);

        var value = reader.GetValue(0);

        var je = Assert.IsType<JsonElement>(value);
        Assert.Equal(JsonValueKind.Array, je.ValueKind);
        Assert.Equal(3, je.GetArrayLength());
    }

    #endregion

    private static CouchbaseDbDataReader<JsonElement> CreateReader(List<JsonElement> rows)
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Success };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.Rows).Returns(rows.ToAsyncEnumerable());

        return new CouchbaseDbDataReader<JsonElement>(mockQueryResult.Object);
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

    private static CouchbaseDbDataReader<JsonElement> CreateReaderWithCancellationToken(
        List<JsonElement> rows,
        CancellationToken cancellationToken)
    {
        var mockQueryResult = new Mock<IQueryResult<JsonElement>>();
        var metaData = new QueryMetaData { Status = QueryStatus.Success };
        mockQueryResult.Setup(q => q.MetaData).Returns(metaData);
        mockQueryResult.Setup(q => q.Rows).Returns(rows.ToAsyncEnumerable());

        return new CouchbaseDbDataReader<JsonElement>(
            mockQueryResult.Object,
            connection: null,
            CommandBehavior.Default,
            cancellationToken);
    }
}
