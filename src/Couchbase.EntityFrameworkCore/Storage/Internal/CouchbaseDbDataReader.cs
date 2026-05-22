using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Couchbase.Query;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// A <see cref="DbDataReader"/> implementation for reading Couchbase N1QL query results.
/// </summary>
/// <remarks>
/// <para>
/// This reader wraps an <see cref="IQueryResult{T}"/> and provides ADO.NET-compatible access to query results.
/// Rows must be <see cref="JsonElement"/> instances (i.e. the query must be executed with
/// <c>cluster.QueryAsync&lt;JsonElement&gt;()</c>); any other row type throws
/// <see cref="NotSupportedException"/> when the first row is materialized — which may occur
/// during an explicit <see cref="ReadAsync"/> call or earlier during schema discovery triggered
/// by accessing <see cref="FieldCount"/>, <see cref="HasRows"/>, or <see cref="GetOrdinal"/>.
/// </para>
/// <para>
/// <b>Schema Discovery:</b> Field metadata (names and ordinals) is captured from the first row and reused for all
/// subsequent rows. If the first row has fields <c>["id", "name"]</c>, those become ordinals 0 and 1 respectively.
/// Later rows with missing fields return <see cref="DBNull.Value"/> for those ordinals; extra fields are
/// inaccessible by ordinal.
/// </para>
/// <para>
/// <b>Value Extraction:</b> Scalar JSON values (string, number, boolean, null) are converted to their CLR
/// equivalents by <see cref="GetValue"/>. JSON objects and arrays are returned as a raw <see cref="JsonElement"/>
/// — no unwrapping is performed. Use <see cref="GetFieldValue{T}"/> with <see cref="JsonElement"/> to work with
/// the raw structure, or with a target CLR type to deserialize it via <see cref="System.Text.Json"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of rows in the query result. Must be <see cref="JsonElement"/>.</typeparam>
public class CouchbaseDbDataReader<T> : DbDataReader
{
    private readonly IQueryResult<T> _queryResult;
    private readonly DbConnection? _connection;
    private readonly CommandBehavior _behavior;
    private readonly CancellationToken _initialCancellationToken;
    // Ordered SELECT projection aliases supplied by the caller (one per shaper ordinal).
    // Used in GetValue to translate shaper ordinal → JSON property via _fieldOrdinals.
    private readonly string?[]? _columnNames;
    // Reverse map of _columnNames: alias (case-insensitive) → projection ordinal.
    // Built eagerly at construction so GetOrdinal needs no schema discovery when active.
    private readonly Dictionary<string, int>? _projectionOrdinals;
    private IAsyncEnumerator<T>? _enumerator;
    private CancellationToken _cancellationToken;
    private T? _currentRow;

    /// <summary>
    /// Gets the current row as read by the last <see cref="ReadAsync"/> call.
    /// </summary>
    public T? CurrentRow => _currentRow;
    private T? _bufferedRow;
    private bool _hasBufferedRow;
    private bool _hasCurrentRow;
    private bool? _hasRows;
    private List<string>? _fieldNames;
    private Dictionary<string, int>? _fieldOrdinals;
    private bool _isClosed;
    private bool _schemaInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseDbDataReader{T}"/> class.
    /// </summary>
    /// <param name="queryResult">The Couchbase query result to read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryResult"/> is null.</exception>
    public CouchbaseDbDataReader(IQueryResult<T> queryResult)
        : this(queryResult, null, CommandBehavior.Default, CancellationToken.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseDbDataReader{T}"/> class with a column-name mapping.
    /// The <paramref name="columnNames"/> array should contain the SELECT-clause alias for each EF Core
    /// <c>readerColumn</c> in order; <c>null</c> elements fall back to positional mapping.
    /// </summary>
    public CouchbaseDbDataReader(IQueryResult<T> queryResult, string?[]? columnNames)
        : this(queryResult, null, CommandBehavior.Default, CancellationToken.None)
    {
        _columnNames = columnNames;
        if (columnNames != null)
        {
            _projectionOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columnNames.Length; i++)
            {
                var alias = columnNames[i];
                if (alias != null)
                    _projectionOrdinals.TryAdd(alias, i);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseDbDataReader{T}"/> class with connection and behavior options.
    /// </summary>
    /// <param name="queryResult">The Couchbase query result to read from.</param>
    /// <param name="connection">The connection to close when the reader is closed (if <see cref="CommandBehavior.CloseConnection"/> is specified).</param>
    /// <param name="behavior">The command behavior flags.</param>
    /// <param name="cancellationToken">The cancellation token used for schema discovery and initial row iteration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryResult"/> is null.</exception>
    public CouchbaseDbDataReader(IQueryResult<T> queryResult, DbConnection? connection, CommandBehavior behavior, CancellationToken cancellationToken)
    {
        _queryResult = queryResult ?? throw new ArgumentNullException(nameof(queryResult));
        _connection = connection;
        _behavior = behavior;
        _initialCancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the number of fields in the current row.
    /// </summary>
    /// <remarks>
    /// Accessing this property before calling <see cref="Read"/> will trigger schema discovery,
    /// which reads and buffers the first row. The buffered row is returned on the next <see cref="Read"/> call.
    /// </remarks>
    public override int FieldCount
    {
        get
        {
            if (_columnNames != null)
                return _columnNames.Length;
            EnsureFieldInfo();
            return _fieldNames?.Count ?? 0;
        }
    }

    /// <summary>
    /// Gets the number of rows changed, inserted, or deleted. Always returns -1 for Couchbase queries.
    /// </summary>
    /// <remarks>
    /// For SELECT queries, this always returns -1. Use <see cref="CouchbaseCommand.ExecuteNonQueryAsync"/>
    /// to get mutation counts for INSERT, UPDATE, DELETE, UPSERT, and MERGE statements.
    /// </remarks>
    public override int RecordsAffected => -1;

    /// <summary>
    /// Gets a value indicating whether the result set contains one or more rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accessing this property before calling <see cref="Read"/> will trigger schema discovery,
    /// which peeks at the first row to determine availability. The peeked row is buffered and
    /// returned on the next <see cref="Read"/> call, so no data is lost.
    /// </para>
    /// <para>
    /// Once determined, the value is cached and remains constant even after all rows are read.
    /// </para>
    /// </remarks>
    public override bool HasRows
    {
        get
        {
            if (_hasRows.HasValue)
            {
                return _hasRows.Value;
            }

            // Peek at first row to determine if there are any rows
            EnsureFieldInfo();
            return _hasRows ?? false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the reader is closed.
    /// </summary>
    public override bool IsClosed => _isClosed;

    /// <summary>
    /// Gets the nesting depth of the current row. Always returns 0 for Couchbase (no nested result sets).
    /// </summary>
    public override int Depth => 0;

    /// <summary>
    /// Gets the value of the specified column by ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value of the column.</returns>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <summary>
    /// Gets the value of the specified column by name.
    /// </summary>
    /// <param name="name">The column name (case-insensitive).</param>
    /// <returns>The value of the column.</returns>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <summary>
    /// Advances the reader to the next row synchronously.
    /// </summary>
    /// <returns><c>true</c> if there are more rows; otherwise, <c>false</c>.</returns>
    public override bool Read()
    {
        return ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously advances the reader to the next row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If schema discovery (via <see cref="FieldCount"/>, <see cref="HasRows"/>, etc.) has already buffered
    /// the first row, that row is returned immediately without advancing the underlying enumerator.
    /// </para>
    /// <para>
    /// The cancellation token is checked before each iteration. If the enumerator was created during
    /// schema discovery, it uses the initial cancellation token provided to the constructor.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns><c>true</c> if there are more rows; otherwise, <c>false</c>.</returns>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (_isClosed)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // If we have a buffered row from schema discovery, return it first
        if (_hasBufferedRow)
        {
            _currentRow = _bufferedRow;
            _bufferedRow = default;
            _hasBufferedRow = false;
            _hasCurrentRow = true;
            return true;
        }

        EnsureEnumerator(cancellationToken);

        var hasMore = await _enumerator!.MoveNextAsync().ConfigureAwait(false);
        if (hasMore)
        {
            _currentRow = _enumerator.Current;
            _hasCurrentRow = true;

            // Set _hasRows on first successful read if not already set
            _hasRows ??= true;

            if (!_schemaInitialized)
            {
                _schemaInitialized = true;
                InitializeFieldInfo();
            }
        }
        else
        {
            _currentRow = default;
            _hasCurrentRow = false;

            // If this is the first read and it returned false, there are no rows
            _hasRows ??= false;
        }

        return hasMore;
    }

    /// <summary>
    /// Lazily creates the enumerator on first use, capturing the cancellation token.
    /// This allows cancellation to abort row iteration.
    /// </summary>
    private void EnsureEnumerator(CancellationToken cancellationToken)
    {
        if (_enumerator == null)
        {
            _cancellationToken = cancellationToken;
            _enumerator = _queryResult.Rows.GetAsyncEnumerator(_cancellationToken);
        }
    }

    /// <summary>
    /// Advances the reader to the next result set. Always returns <c>false</c> for Couchbase.
    /// </summary>
    /// <remarks>
    /// Couchbase N1QL queries return a single result set. This method always returns <c>false</c>.
    /// </remarks>
    /// <returns>Always <c>false</c>.</returns>
    public override bool NextResult()
    {
        // Couchbase queries return a single result set
        return false;
    }

    /// <summary>
    /// Asynchronously advances the reader to the next result set. Always returns <c>false</c> for Couchbase.
    /// </summary>
    /// <remarks>
    /// Couchbase N1QL queries return a single result set. This method always returns <c>false</c>.
    /// </remarks>
    /// <param name="cancellationToken">Ignored. Couchbase does not support multiple result sets.</param>
    /// <returns>Always <c>false</c>.</returns>
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Closes the reader and releases resources.
    /// </summary>
    /// <remarks>
    /// If <see cref="CommandBehavior.CloseConnection"/> was specified when creating the reader,
    /// the associated connection is also closed.
    /// </remarks>
    public override void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _enumerator?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            // Honor CloseConnection behavior
            if ((_behavior & CommandBehavior.CloseConnection) != 0 && _connection != null)
            {
                _connection.Close();
            }
        }
    }

    /// <summary>
    /// Asynchronously closes the reader and releases resources.
    /// </summary>
    /// <remarks>
    /// If <see cref="CommandBehavior.CloseConnection"/> was specified when creating the reader,
    /// the associated connection is also closed asynchronously.
    /// </remarks>
    public override async Task CloseAsync()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            if (_enumerator != null)
            {
                await _enumerator.DisposeAsync().ConfigureAwait(false);
            }

            // Honor CloseConnection behavior
            if ((_behavior & CommandBehavior.CloseConnection) != 0 && _connection != null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the name of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Field names are captured from the first row during schema discovery. Accessing this method
    /// before calling <see cref="Read"/> will trigger schema discovery.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The name of the field.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="ordinal"/> is out of range.</exception>
    public override string GetName(int ordinal)
    {
        if (_columnNames != null)
        {
            if ((uint)ordinal >= (uint)_columnNames.Length)
                throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range. FieldCount: {_columnNames.Length}");
            var alias = _columnNames[ordinal];
            if (alias != null)
                return alias;
            // null slot: fall back to the JSON field name at the same position
        }

        EnsureFieldInfo();
        ValidateOrdinal(ordinal);
        return _fieldNames![ordinal];
    }

    /// <summary>
    /// Gets the ordinal of the field with the specified name.
    /// </summary>
    /// <remarks>
    /// Field name lookup is case-insensitive. Accessing this method before calling <see cref="Read"/>
    /// will trigger schema discovery.
    /// </remarks>
    /// <param name="name">The name of the field.</param>
    /// <returns>The zero-based ordinal of the field.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown when the field is not found.</exception>
    public override int GetOrdinal(string name)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        // When projection aliases are active, resolve against them to return the
        // projection ordinal that GetValue/GetName expect.  No schema discovery needed.
        if (_projectionOrdinals != null)
        {
            if (_projectionOrdinals.TryGetValue(name, out var projOrdinal))
                return projOrdinal;

            // Fallback for null-slot positions: GetName(i) returns the JSON field name for
            // a null slot, so GetOrdinal must resolve it back to i to keep
            // GetOrdinal(GetName(i)) == i. Bounds-check against _columnNames.Length so that
            // extra JSON fields beyond the projection are not surfaced, and verify the slot
            // is actually null so non-null aliases that somehow bypass _projectionOrdinals
            // are rejected rather than silently returned.
            EnsureFieldInfo();
            if (_fieldOrdinals != null && _fieldOrdinals.TryGetValue(name, out var jsonOrd)
                && (uint)jsonOrd < (uint)_columnNames!.Length
                && _columnNames[jsonOrd] == null)
            {
                return jsonOrd;
            }

            throw new IndexOutOfRangeException($"Field '{name}' not found.");
        }

        EnsureFieldInfo();
        if (_fieldOrdinals != null && _fieldOrdinals.TryGetValue(name, out var ordinal))
            return ordinal;

        // For scalar SELECT RAW results the single synthetic field has an empty name.
        // Any column name lookup on a scalar result maps to ordinal 0.
        if (_fieldNames?.Count == 1 && _fieldNames[0] == string.Empty)
            return 0;

        throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }

    /// <summary>
    /// Gets the data type name of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Returns the CLR type name of the current value. Since Couchbase is schemaless,
    /// the type may vary between rows for the same field.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The CLR type name of the value, or "Object" if null.</returns>
    public override string GetDataTypeName(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType().Name ?? "Object";
    }

    /// <summary>
    /// Gets the CLR type of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Returns the runtime type of the current value. Since Couchbase is schemaless,
    /// the type may vary between rows for the same field.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The CLR type of the value, or <see cref="object"/> if null.</returns>
    public override Type GetFieldType(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType() ?? typeof(object);
    }

    /// <summary>
    /// Gets the value of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Scalar JSON values (string, number, boolean, null) are returned as their CLR equivalents.
    /// JSON objects and arrays are returned as a raw <see cref="JsonElement"/> — no unwrapping is
    /// performed. Use <see cref="GetFieldValue{T}"/> with a target type to deserialize complex values.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value of the field, or <see cref="DBNull.Value"/> if null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no current row exists (call <see cref="Read"/> first).</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="ordinal"/> is out of range.</exception>
    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();

        // When the caller supplied column names (projection aliases from the SELECT clause),
        // look up the JSON property directly by alias using a case-insensitive scan.
        // This eliminates the _fieldOrdinals → _fieldNames round-trip that previously
        // translated the alias to a json ordinal and back to the same name.
        if (_columnNames != null && (uint)ordinal < (uint)_columnNames.Length)
        {
            var colName = _columnNames[ordinal];
            if (colName != null)
            {
                if (_currentRow is JsonElement je && je.ValueKind == JsonValueKind.Object)
                {
                    return TryGetPropertyCI(je, colName, out var prop)
                        ? ConvertJsonElement(prop)
                        : DBNull.Value;
                }
                // Scalar SELECT RAW row: the entire element is the value.
                if (_currentRow is JsonElement raw)
                    return ConvertJsonElement(raw);

                return DBNull.Value;
            }
            // null slot: no alias for this ordinal — fall through to positional access.
        }

        // No column names supplied, or null slot: positional access via schema discovery.
        var isNullSlot = _columnNames != null && (uint)ordinal < (uint)_columnNames.Length;
        if (isNullSlot && (_fieldNames == null || ordinal >= _fieldNames.Count))
            return DBNull.Value;
        ValidateOrdinal(ordinal);
        return GetFieldValue(_fieldNames![ordinal]);
    }

    /// <summary>
    /// Populates an array with the values of all fields in the current row.
    /// </summary>
    /// <param name="values">The array to populate with field values.</param>
    /// <returns>The number of values copied (minimum of array length and field count).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="values"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no current row exists.</exception>
    public override int GetValues(object[] values)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        EnsureCurrentRow();

        int fieldCount;
        if (_columnNames != null)
        {
            fieldCount = _columnNames.Length;
        }
        else
        {
            EnsureFieldInfo();
            fieldCount = _fieldNames!.Count;
        }

        var count = Math.Min(values.Length, fieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    /// <summary>
    /// Determines whether the field at the specified ordinal is null or <see cref="DBNull.Value"/>.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns><c>true</c> if the field is null or DBNull; otherwise, <c>false</c>.</returns>
    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value == null || value == DBNull.Value;
    }

    /// <summary>
    /// Asynchronously determines whether the field at the specified ordinal is null.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="cancellationToken">Ignored. The operation is synchronous.</param>
    /// <returns><c>true</c> if the field is null or DBNull; otherwise, <c>false</c>.</returns>
    public override async Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        return IsDBNull(ordinal);
    }

    /// <summary>
    /// Gets the boolean value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The boolean value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to boolean.</exception>
    public override bool GetBoolean(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Boolean."),
            _ => Convert.ToBoolean(value)
        };
    }

    /// <summary>
    /// Gets the byte value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The byte value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to byte.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the byte range (0-255).</exception>
    public override byte GetByte(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            byte b => b,
            int i => Convert.ToByte(i),
            long l => Convert.ToByte(l),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetByte(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Byte."),
            _ => Convert.ToByte(value)
        };
    }

    /// <summary>
    /// Reads bytes from the field at the specified ordinal into a buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The field value must be a byte array or a base64-encoded string. If <paramref name="buffer"/>
    /// is null, returns the total length of the byte data without copying.
    /// </para>
    /// <para>
    /// If <paramref name="dataOffset"/> is beyond the source data length, returns 0 (ADO.NET behavior).
    /// </para>
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="dataOffset">The offset within the source data to start reading from.</param>
    /// <param name="buffer">The buffer to copy bytes into, or null to query the total length.</param>
    /// <param name="bufferOffset">The offset within the buffer to start writing to.</param>
    /// <param name="length">The maximum number of bytes to copy.</param>
    /// <returns>The number of bytes copied, or the total length if buffer is null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for negative offsets or lengths, or buffer overflow.</exception>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to bytes.</exception>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataOffset), dataOffset, "Data offset cannot be negative.");
        }

        var value = GetValue(ordinal);
        byte[] bytes = value switch
        {
            byte[] b => b,
            string s => Convert.FromBase64String(s),
            JsonElement je when je.ValueKind == JsonValueKind.String => Convert.FromBase64String(je.GetString()!),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name} to byte array.")
        };

        // If buffer is null, return total length (used to query size)
        if (buffer == null)
        {
            return bytes.Length;
        }

        if (bufferOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset cannot be negative.");
        }

        if (bufferOffset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset exceeds buffer length.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length cannot be negative.");
        }

        // If dataOffset is at or beyond the source data, return 0 (ADO.NET behavior)
        if (dataOffset >= bytes.Length)
        {
            return 0;
        }

        var sourceOffset = (int)dataOffset;
        var availableBytes = bytes.Length - sourceOffset;
        var availableBuffer = buffer.Length - bufferOffset;
        var bytesToCopy = Math.Min(length, Math.Min(availableBytes, availableBuffer));

        if (bytesToCopy > 0)
        {
            Array.Copy(bytes, sourceOffset, buffer, bufferOffset, bytesToCopy);
        }

        return bytesToCopy;
    }

    /// <summary>
    /// Gets the character value of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// For string values, returns the first character. Throws if the string is empty.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The character value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to char.</exception>
    public override char GetChar(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            char c => c,
            string s when s.Length > 0 => s[0],
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()![0],
            _ => Convert.ToChar(value)
        };
    }

    /// <summary>
    /// Reads characters from the field at the specified ordinal into a buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <paramref name="buffer"/> is null, returns the total length of the string without copying.
    /// </para>
    /// <para>
    /// If <paramref name="dataOffset"/> is beyond the source string length, returns 0 (ADO.NET behavior).
    /// </para>
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="dataOffset">The offset within the source string to start reading from.</param>
    /// <param name="buffer">The buffer to copy characters into, or null to query the total length.</param>
    /// <param name="bufferOffset">The offset within the buffer to start writing to.</param>
    /// <param name="length">The maximum number of characters to copy.</param>
    /// <returns>The number of characters copied, or the total length if buffer is null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for negative offsets or lengths, or buffer overflow.</exception>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataOffset), dataOffset, "Data offset cannot be negative.");
        }

        var str = GetString(ordinal);

        // If buffer is null, return total length (used to query size)
        if (buffer == null)
        {
            return str.Length;
        }

        if (bufferOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset cannot be negative.");
        }

        if (bufferOffset > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset exceeds buffer length.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length cannot be negative.");
        }

        // If dataOffset is at or beyond the source data, return 0 (ADO.NET behavior)
        if (dataOffset >= str.Length)
        {
            return 0;
        }

        var sourceOffset = (int)dataOffset;
        var availableChars = str.Length - sourceOffset;
        var availableBuffer = buffer.Length - bufferOffset;
        var charsToCopy = Math.Min(length, Math.Min(availableChars, availableBuffer));

        if (charsToCopy > 0)
        {
            str.CopyTo(sourceOffset, buffer, bufferOffset, charsToCopy);
        }

        return charsToCopy;
    }

    /// <summary>
    /// Gets the DateTime value of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Supports ISO 8601 date strings, <see cref="DateTime"/>, and <see cref="DateTimeOffset"/> values.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The DateTime value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to DateTime.</exception>
    /// <exception cref="FormatException">Thrown when a string value is not a valid date format.</exception>
    public override DateTime GetDateTime(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            string s => DateTime.Parse(s),
            JsonElement je when je.ValueKind == JsonValueKind.String => DateTime.Parse(je.GetString()!),
            _ => Convert.ToDateTime(value)
        };
    }

    /// <summary>
    /// Gets the decimal value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The decimal value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to decimal.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the decimal range.</exception>
    public override decimal GetDecimal(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            decimal d => d,
            double dbl => Convert.ToDecimal(dbl),
            long l => Convert.ToDecimal(l),
            int i => Convert.ToDecimal(i),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDecimal(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Decimal."),
            _ => Convert.ToDecimal(value)
        };
    }

    /// <summary>
    /// Gets the double value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The double value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to double.</exception>
    public override double GetDouble(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            double d => d,
            long l => Convert.ToDouble(l),
            int i => Convert.ToDouble(i),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Double."),
            _ => Convert.ToDouble(value)
        };
    }

    /// <summary>
    /// Gets the float value of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Values exceeding float range may return <see cref="float.PositiveInfinity"/> or <see cref="float.NegativeInfinity"/>.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The float value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to float.</exception>
    public override float GetFloat(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            float f => f,
            double d => Convert.ToSingle(d),
            long l => Convert.ToSingle(l),
            int i => Convert.ToSingle(i),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetSingle(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Single."),
            _ => Convert.ToSingle(value)
        };
    }

    /// <summary>
    /// Gets the GUID value of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// Supports GUID strings in standard formats (e.g., "d85b1407-351d-4694-9392-03acc5870eb1").
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The GUID value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to GUID.</exception>
    /// <exception cref="FormatException">Thrown when a string value is not a valid GUID format.</exception>
    public override Guid GetGuid(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            JsonElement je when je.ValueKind == JsonValueKind.String => Guid.Parse(je.GetString()!),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name} to Guid.")
        };
    }

    /// <summary>
    /// Gets the 16-bit signed integer value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The Int16 value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to Int16.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the Int16 range (-32768 to 32767).</exception>
    public override short GetInt16(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            short s => s,
            int i => Convert.ToInt16(i),
            long l => Convert.ToInt16(l),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt16(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Int16."),
            _ => Convert.ToInt16(value)
        };
    }

    /// <summary>
    /// Gets the 32-bit signed integer value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The Int32 value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to Int32.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the Int32 range.</exception>
    public override int GetInt32(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            int i => i,
            long l => Convert.ToInt32(l),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Int32."),
            _ => Convert.ToInt32(value)
        };
    }

    /// <summary>
    /// Gets the 64-bit signed integer value of the field at the specified ordinal.
    /// </summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The Int64 value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to Int64.</exception>
    /// <exception cref="OverflowException">Thrown when the value is outside the Int64 range.</exception>
    public override long GetInt64(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            long l => l,
            int i => i,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt64(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Int64."),
            _ => Convert.ToInt64(value)
        };
    }

    /// <summary>
    /// Gets the string value of the field at the specified ordinal.
    /// </summary>
    /// <remarks>
    /// For non-string JSON values, returns the raw JSON text representation.
    /// </remarks>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The string value of the field.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value is null.</exception>
    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()!,
            JsonElement je => je.GetRawText(),
            // EF Core's ShapedQueryCompilingExpressionVisitor emits materializer lambdas that
            // call GetString directly (no IsDBNull guard) for non-nullable string properties,
            // even when those properties are mapped from optional N1QL columns that can be
            // absent/null. Returning null! here lets those materializers propagate the absence
            // as a CLR null; callers that need strict ADO.NET semantics must call IsDBNull first.
            DBNull => null!,
            null => throw new InvalidCastException("Cannot convert null to string."),
            _ => value.ToString()!
        };
    }

    /// <summary>
    /// Gets the value of the field at the specified ordinal as the specified type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method can deserialize JSON objects and arrays to complex types using <see cref="System.Text.Json"/>.
    /// Use this to access nested document structures that would otherwise be extracted/flattened by <see cref="GetValue"/>.
    /// </para>
    /// <para>
    /// Example: <c>reader.GetFieldValue&lt;JsonElement&gt;(0)</c> returns the raw JSON element,
    /// or <c>reader.GetFieldValue&lt;MyClass&gt;(0)</c> deserializes to a custom type.
    /// </para>
    /// </remarks>
    /// <typeparam name="T1">The type to return.</typeparam>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value cast or deserialized to the specified type.</returns>
    /// <exception cref="InvalidCastException">Thrown when conversion fails.</exception>
    public override T1 GetFieldValue<T1>(int ordinal)
    {
        var value = GetValue(ordinal);

        if (value is T1 typedValue)
        {
            return typedValue;
        }

        if (value is JsonElement je)
        {
            return je.Deserialize<T1>()!;
        }

        return (T1)Convert.ChangeType(value, typeof(T1))!;
    }

    /// <summary>
    /// Returns a <see cref="DataTable"/> that describes the column metadata of the result set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema is derived from the first row. Since Couchbase is schemaless, all columns are
    /// reported as <see cref="object"/> type with <c>AllowDBNull = true</c>.
    /// </para>
    /// <para>
    /// Accessing this method before calling <see cref="Read"/> will trigger schema discovery.
    /// </para>
    /// </remarks>
    /// <returns>A DataTable with ColumnName, ColumnOrdinal, DataType, and AllowDBNull columns.</returns>
    public override DataTable GetSchemaTable()
    {
        var schemaTable = new DataTable("SchemaTable");
        schemaTable.Columns.Add("ColumnName", typeof(string));
        schemaTable.Columns.Add("ColumnOrdinal", typeof(int));
        schemaTable.Columns.Add("DataType", typeof(Type));
        schemaTable.Columns.Add("AllowDBNull", typeof(bool));

        if (_columnNames != null)
        {
            for (var i = 0; i < _columnNames.Length; i++)
            {
                var row = schemaTable.NewRow();
                row["ColumnName"] = (object?)_columnNames[i] ?? DBNull.Value;
                row["ColumnOrdinal"] = i;
                row["DataType"] = typeof(object);
                row["AllowDBNull"] = true;
                schemaTable.Rows.Add(row);
            }
            return schemaTable;
        }

        EnsureFieldInfo();
        if (_fieldNames != null)
        {
            for (var i = 0; i < _fieldNames.Count; i++)
            {
                var row = schemaTable.NewRow();
                row["ColumnName"] = _fieldNames[i];
                row["ColumnOrdinal"] = i;
                row["DataType"] = typeof(object);
                row["AllowDBNull"] = true;
                schemaTable.Rows.Add(row);
            }
        }

        return schemaTable;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the rows of the result set.
    /// </summary>
    /// <returns>An <see cref="IEnumerator"/> for the rows.</returns>
    public override IEnumerator GetEnumerator()
    {
        return new DbEnumerator(this, closeReader: false);
    }

    private void EnsureCurrentRow()
    {
        if (!_hasCurrentRow)
        {
            throw new InvalidOperationException("No current row. Call Read() first.");
        }
    }

    /// <summary>
    /// Ensures field metadata is available, reading and buffering the first row if necessary.
    /// The buffered row will be returned on the next explicit Read() call, so no data is lost.
    /// Also sets _hasRows to indicate whether at least one row exists.
    /// </summary>
    private void EnsureFieldInfo()
    {
        if (_schemaInitialized || _isClosed)
        {
            return;
        }

        // Check for cancellation before starting schema discovery
        _initialCancellationToken.ThrowIfCancellationRequested();

        // Create enumerator using initial cancellation token so schema discovery can be cancelled
        EnsureEnumerator(_initialCancellationToken);

        // Read the first row to discover schema, but buffer it so it's not lost
        var hasRow = _enumerator!.MoveNextAsync().AsTask().GetAwaiter().GetResult();
        _hasRows = hasRow;

        if (hasRow)
        {
            _bufferedRow = _enumerator.Current;
            _hasBufferedRow = true;
            _currentRow = _bufferedRow; // Set current row so InitializeFieldInfo can access it
            _schemaInitialized = true;
            InitializeFieldInfo();
            _currentRow = default; // Clear current row - caller must call Read() to access data
        }
        else
        {
            // No rows - initialize empty schema
            _schemaInitialized = true;
            _fieldNames = new List<string>();
            _fieldOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Initializes field metadata (names and ordinals) from the first row.
    /// 
    /// LIMITATION: Field schema is captured once from the first row and reused for all subsequent rows.
    /// This means:
    /// - If later rows have MISSING fields, GetValue returns DBNull.Value for those ordinals
    /// - If later rows have EXTRA fields, they are inaccessible (not in the ordinal mapping)
    /// - If later rows have DIFFERENT field order, ordinal-based access still uses the first row's order
    /// 
    /// This behavior is consistent with ADO.NET's expectation of a stable schema per result set,
    /// but may not suit heterogeneous document collections where each document has a different shape.
    /// For such cases, consider using name-based access (reader["fieldName"]) and checking IsDBNull.
    /// </summary>
    private void InitializeFieldInfo()
    {
        _fieldNames = new List<string>();
        _fieldOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (_currentRow is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var ordinal = 0;
            foreach (var property in je.EnumerateObject())
            {
                _fieldNames.Add(property.Name);
                _fieldOrdinals[property.Name] = ordinal++;
            }
        }
        else if (_currentRow is JsonElement)
        {
            // Non-object scalar from SELECT RAW (e.g. COUNT result) — single synthetic field at ordinal 0
            _fieldNames.Add(string.Empty);
            _fieldOrdinals[string.Empty] = 0;
        }
        else if (_currentRow != null)
        {
            throw new NotSupportedException(
                $"CouchbaseDbDataReader<T> requires rows of type JsonElement but received {_currentRow.GetType().FullName}. " +
                "Use cluster.QueryAsync<JsonElement>() to produce a compatible result.");
        }
    }

    private void ValidateOrdinal(int ordinal)
    {
        if (_fieldNames == null || ordinal < 0 || ordinal >= _fieldNames.Count)
        {
            throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range. FieldCount: {_fieldNames?.Count ?? 0}");
        }
    }

    private object GetFieldValue(string fieldName)
    {
        if (_currentRow is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(fieldName, out var property))
            {
                return ConvertJsonElement(property);
            }
            // For RAW queries, the entire element is the value
            if (_fieldNames?.Count == 1)
            {
                return ConvertJsonElement(je);
            }
        }
        return DBNull.Value;
    }

    private static bool TryGetPropertyCI(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var prop in element.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = prop.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null      => DBNull.Value,
            JsonValueKind.Undefined => DBNull.Value,
            JsonValueKind.True      => true,
            JsonValueKind.False     => false,
            JsonValueKind.String    => element.GetString()!,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number    => element.GetDouble(),
            _                       => element
        };
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
