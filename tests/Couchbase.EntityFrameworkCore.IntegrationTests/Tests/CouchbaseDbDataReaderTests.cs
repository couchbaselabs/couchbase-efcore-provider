using System.Data;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseDbDataReaderTests(
    BloggingFixture bloggingFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task ExecuteReaderAsync_ReturnsDataReader()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id, 'test' AS name";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.NotNull(reader);
        Assert.IsType<CouchbaseDbDataReader<object>>(reader);
    }

    [Fact]
    public async Task Read_WithRows_ReturnsTrue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync());
    }

    [Fact]
    public async Task Read_WithNoRows_ReturnsFalse()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM `default`.`blogs`.`blog` WHERE blogId = -99999";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task FieldCount_ReturnsCorrectCount()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS col1, 2 AS col2, 3 AS col3";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(3, reader.FieldCount);
    }

    [Fact]
    public async Task GetName_ReturnsFieldName()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS myColumn";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal("myColumn", reader.GetName(0));
    }

    [Fact]
    public async Task GetOrdinal_ReturnsCorrectOrdinal()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'a' AS fir, 'b' AS sec";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(0, reader.GetOrdinal("fir"));
        Assert.Equal(1, reader.GetOrdinal("sec"));
    }

    [Fact]
    public async Task GetInt64_ReturnsLongValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(42L, reader.GetInt64(0));
    }

    [Fact]
    public async Task GetString_ReturnsStringValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'hello world' AS greeting";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal("hello world", reader.GetString(0));
    }

    [Fact]
    public async Task GetBoolean_ReturnsBooleanValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TRUE AS flag";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.True(reader.GetBoolean(0));
    }

    [Fact]
    public async Task GetDouble_ReturnsDoubleValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 3.14159 AS pi";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(3.14159, reader.GetDouble(0), 5);
    }

    [Fact]
    public async Task IsDBNull_WithNullValue_ReturnsTrue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT NULL AS nullVal";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task IsDBNull_WithValue_ReturnsFalse()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.False(reader.IsDBNull(0));
    }

    [Fact]
    public async Task GetValue_ReturnsValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 123 AS num, 'text' AS str";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(123L, reader.GetValue(0));
        Assert.Equal("text", reader.GetValue(1));
    }

    [Fact]
    public async Task Indexer_ByOrdinal_ReturnsValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(42L, reader[0]);
    }

    [Fact]
    public async Task Indexer_ByName_ReturnsValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(42L, reader["num"]);
    }

    [Fact]
    public async Task GetValues_FillsArray()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS a, 'b' AS b, TRUE AS c";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        var values = new object[3];
        var count = reader.GetValues(values);

        Assert.Equal(3, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal("b", values[1]);
        Assert.True((bool)values[2]);
    }

    [Fact]
    public async Task MultipleRows_CanIterate()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM (SELECT 1 AS id UNION SELECT 2 UNION SELECT 3) AS t";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var rowCount = 0;
        while (await reader.ReadAsync())
        {
            rowCount++;
            outputHelper.WriteLine($"Row {rowCount}: id = {reader.GetInt64(0)}");
        }

        Assert.Equal(3, rowCount);
    }

    [Fact]
    public async Task NextResult_ReturnsFalse()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.False(reader.NextResult());
    }

    [Fact]
    public async Task Close_SetsIsClosed()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id";

        var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.False(reader.IsClosed);

        reader.Close();
        Assert.True(reader.IsClosed);
    }

    [Fact]
    public async Task Depth_ReturnsZero()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.Equal(0, reader.Depth);
    }

    [Fact]
    public async Task RecordsAffected_ReturnsNegativeOne()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public async Task GetSchemaTable_ReturnsSchema()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS col1, 'test' AS col2";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        var schemaTable = reader.GetSchemaTable();

        Assert.NotNull(schemaTable);
        Assert.Equal(2, schemaTable.Rows.Count);
        Assert.Equal("col1", schemaTable.Rows[0]["ColumnName"]);
        Assert.Equal("col2", schemaTable.Rows[1]["ColumnName"]);
    }

    [Fact]
    public async Task GetDataTypeName_ReturnsTypeName()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        var typeName = reader.GetDataTypeName(0);
        outputHelper.WriteLine($"DataTypeName: {typeName}");
        Assert.NotNull(typeName);
    }

    [Fact]
    public async Task GetFieldType_ReturnsType()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        var fieldType = reader.GetFieldType(0);
        outputHelper.WriteLine($"FieldType: {fieldType.Name}");
        Assert.NotNull(fieldType);
    }

    [Fact]
    public async Task HasRows_WithSuccessfulQuery_ReturnsTrue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 AS id";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task GetDecimal_ReturnsDecimalValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 123.45 AS amount";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(123.45m, reader.GetDecimal(0));
    }

    [Fact]
    public async Task GetInt32_ReturnsIntValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS num";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync();

        Assert.Equal(42, reader.GetInt32(0));
    }

    [Fact]
    public async Task QueryFromBlogsTable_CanReadFields()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT blogId, url FROM `default`.`blogs`.`blog` LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        if (await reader.ReadAsync())
        {
            var blogId = reader["blogId"];
            var url = reader["url"];
            outputHelper.WriteLine($"BlogId: {blogId}, Url: {url}");
            Assert.NotNull(blogId);
        }
    }

    [Fact]
    public async Task ComplexObject_WithMultipleProperties_CanReadAllTypes()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                42 AS intValue,
                9223372036854775807 AS longValue,
                3.14159 AS doubleValue,
                'Hello World' AS stringValue,
                TRUE AS boolTrue,
                FALSE AS boolFalse,
                NULL AS nullValue,
                123.45 AS decimalValue";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        // Verify field count
        Assert.Equal(8, reader.FieldCount);

        // Verify field names
        Assert.Equal("intValue", reader.GetName(0));
        Assert.Equal("longValue", reader.GetName(1));
        Assert.Equal("doubleValue", reader.GetName(2));
        Assert.Equal("stringValue", reader.GetName(3));
        Assert.Equal("boolTrue", reader.GetName(4));
        Assert.Equal("boolFalse", reader.GetName(5));
        Assert.Equal("nullValue", reader.GetName(6));
        Assert.Equal("decimalValue", reader.GetName(7));

        // Verify values by ordinal
        Assert.Equal(42, reader.GetInt32(0));
        Assert.Equal(9223372036854775807L, reader.GetInt64(1));
        Assert.Equal(3.14159, reader.GetDouble(2), 5);
        Assert.Equal("Hello World", reader.GetString(3));
        Assert.True(reader.GetBoolean(4));
        Assert.False(reader.GetBoolean(5));
        Assert.True(reader.IsDBNull(6));
        Assert.Equal(123.45m, reader.GetDecimal(7));

        // Verify values by name using indexer
        Assert.Equal(42L, reader["intValue"]);
        Assert.Equal("Hello World", reader["stringValue"]);
        Assert.True((bool)reader["boolTrue"]);

        outputHelper.WriteLine("Successfully read all property types from complex object");
    }

    [Fact]
    public async Task ComplexObject_WithDateTimeString_CanParseDateTimes()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                '2025-06-15T14:30:00Z' AS isoDate,
                '2025-12-25' AS dateOnly,
                'a1b2c3d4-e5f6-7890-abcd-ef1234567890' AS guidValue";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        var dateTime = reader.GetDateTime(0);
        Assert.Equal(2025, dateTime.Year);
        Assert.Equal(6, dateTime.Month);
        Assert.Equal(15, dateTime.Day);

        var dateOnly = reader.GetDateTime(1);
        Assert.Equal(2025, dateOnly.Year);
        Assert.Equal(12, dateOnly.Month);
        Assert.Equal(25, dateOnly.Day);

        var guid = reader.GetGuid(2);
        Assert.Equal(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), guid);

        outputHelper.WriteLine($"DateTime: {dateTime}, DateOnly: {dateOnly}, Guid: {guid}");
    }

    [Fact]
    public async Task ComplexObject_WithNestedProperties_CanAccessViaGetValue()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                {'name': 'John', 'age': 30} AS person,
                [1, 2, 3] AS numbers,
                'simple' AS simpleValue";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        Assert.Equal(3, reader.FieldCount);

        // Nested object - extracts first value recursively
        var personValue = reader.GetValue(0);
        outputHelper.WriteLine($"Person value: {personValue} (type: {personValue?.GetType().Name})");
        Assert.NotNull(personValue);

        // Array - extracts first element
        var numbersValue = reader.GetValue(1);
        outputHelper.WriteLine($"Numbers value: {numbersValue} (type: {numbersValue?.GetType().Name})");
        Assert.NotNull(numbersValue);

        // Simple value
        Assert.Equal("simple", reader.GetString(2));
    }

    [Fact]
    public async Task ComplexObject_MixedNumericTypes_CanConvertBetweenTypes()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                100 AS smallInt,
                30000 AS mediumInt,
                3000000000 AS largeInt,
                1.5 AS floatNum,
                99.99 AS price";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        // Read as different numeric types with conversions
        Assert.Equal((byte)100, reader.GetByte(0));
        Assert.Equal((short)100, reader.GetInt16(0));
        Assert.Equal(100, reader.GetInt32(0));
        Assert.Equal(100L, reader.GetInt64(0));

        Assert.Equal((short)30000, reader.GetInt16(1));
        Assert.Equal(30000, reader.GetInt32(1));

        Assert.Equal(3000000000L, reader.GetInt64(2));

        Assert.Equal(1.5f, reader.GetFloat(3), 1);
        Assert.Equal(1.5, reader.GetDouble(3), 1);

        Assert.Equal(99.99m, reader.GetDecimal(4));
        Assert.Equal(99.99, reader.GetDouble(4), 2);

        outputHelper.WriteLine("Successfully converted between numeric types");
    }

    [Fact]
    public async Task ComplexObject_WithManyNulls_HandlesNullsCorrectly()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                NULL AS nullString,
                NULL AS nullInt,
                NULL AS nullBool,
                'notNull' AS hasValue,
                NULL AS nullDate";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
        Assert.False(reader.IsDBNull(3));
        Assert.True(reader.IsDBNull(4));

        Assert.True(await reader.IsDBNullAsync(0, CancellationToken.None));

        // GetValue returns DBNull.Value for nulls
        Assert.Equal(DBNull.Value, reader.GetValue(0));
        Assert.Equal(DBNull.Value, reader.GetValue(1));
        Assert.Equal("notNull", reader.GetValue(3));

        outputHelper.WriteLine("Successfully handled NULL values");
    }

    [Fact]
    public async Task ComplexObject_GetValuesArray_FillsCorrectly()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                1 AS id,
                'Product A' AS name,
                29.99 AS price,
                TRUE AS inStock,
                100 AS quantity";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        // Get all values at once
        var values = new object[5];
        var count = reader.GetValues(values);

        Assert.Equal(5, count);
        Assert.Equal(1L, values[0]);
        Assert.Equal("Product A", values[1]);
        Assert.Equal(29.99, values[2]);
        Assert.True((bool)values[3]);
        Assert.Equal(100L, values[4]);

        // Partial array
        var partialValues = new object[3];
        var partialCount = reader.GetValues(partialValues);

        Assert.Equal(3, partialCount);
        Assert.Equal(1L, partialValues[0]);
        Assert.Equal("Product A", partialValues[1]);
        Assert.Equal(29.99, partialValues[2]);

        outputHelper.WriteLine("Successfully filled values array");
    }

    [Fact]
    public async Task ComplexObject_WithSpecialCharacters_HandlesStringsCorrectly()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                'Hello\nWorld' AS withNewline,
                'Tab\there' AS withTab,
                'Quote: ""test""' AS withQuotes,
                'Unicode: \u00E9\u00E8\u00EA' AS withUnicode,
                '' AS emptyString";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        var withNewline = reader.GetString(0);
        var withTab = reader.GetString(1);
        var withQuotes = reader.GetString(2);
        var withUnicode = reader.GetString(3);
        var emptyString = reader.GetString(4);

        Assert.Contains("\n", withNewline);
        Assert.Contains("\t", withTab);
        Assert.Contains("\"", withQuotes);
        Assert.Equal("", emptyString);

        outputHelper.WriteLine($"Newline: {withNewline.Replace("\n", "\\n")}");
        outputHelper.WriteLine($"Tab: {withTab.Replace("\t", "\\t")}");
        outputHelper.WriteLine($"Quotes: {withQuotes}");
        outputHelper.WriteLine($"Unicode: {withUnicode}");
    }

    [Fact]
    public async Task ComplexObject_GetChars_ReturnsCharacterData()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'ABCDEFGHIJ' AS chars";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        // Get total length
        var totalLength = reader.GetChars(0, 0, null, 0, 0);
        Assert.Equal(10, totalLength);

        // Get partial chars
        var buffer = new char[5];
        var charsRead = reader.GetChars(0, 2, buffer, 0, 5);

        Assert.Equal(5, charsRead);
        Assert.Equal('C', buffer[0]);
        Assert.Equal('D', buffer[1]);
        Assert.Equal('E', buffer[2]);
        Assert.Equal('F', buffer[3]);
        Assert.Equal('G', buffer[4]);

        // GetChar returns first character
        Assert.Equal('A', reader.GetChar(0));

        outputHelper.WriteLine($"Read {charsRead} chars: {new string(buffer)}");
    }

    [Fact]
    public async Task ComplexObject_FieldMetadata_ReturnsCorrectInfo()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                42 AS intField,
                'text' AS stringField,
                TRUE AS boolField,
                3.14 AS doubleField";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync());

        // GetDataTypeName
        outputHelper.WriteLine($"intField type name: {reader.GetDataTypeName(0)}");
        outputHelper.WriteLine($"stringField type name: {reader.GetDataTypeName(1)}");
        outputHelper.WriteLine($"boolField type name: {reader.GetDataTypeName(2)}");
        outputHelper.WriteLine($"doubleField type name: {reader.GetDataTypeName(3)}");

        // GetFieldType
        Assert.Equal(typeof(long), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
        Assert.Equal(typeof(bool), reader.GetFieldType(2));
        Assert.Equal(typeof(double), reader.GetFieldType(3));

        // Ordinal lookups are case-insensitive
        Assert.Equal(0, reader.GetOrdinal("intField"));
        Assert.Equal(0, reader.GetOrdinal("INTFIELD"));
        Assert.Equal(0, reader.GetOrdinal("IntField"));
    }
}
