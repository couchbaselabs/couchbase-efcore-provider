using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
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
/// <see cref="NotSupportedException"/> when a row is materialized via <see cref="PrimeAsync"/>
/// or <see cref="ReadAsync"/>.
/// </para>
/// <para>
/// <b>Column names:</b> When <paramref name="columnNames"/> is supplied the reader maps each
/// projection alias to its shaper ordinal at construction time. Field-name lookup and
/// <see cref="FieldCount"/> are O(1) and work before the first <see cref="ReadAsync"/> call.
/// Null slots in <paramref name="columnNames"/> indicate positional (unaliased) columns; those
/// resolve against the current row's JSON property order and therefore require a prior
/// <see cref="ReadAsync"/> call.
/// </para>
/// <para>
/// When <paramref name="columnNames"/> is not supplied (raw ADO.NET path via
/// <see cref="CouchbaseCommand"/>), field-name and ordinal access resolve positionally from the
/// current row and also require a prior <see cref="ReadAsync"/> call.
/// </para>
/// <para>
/// <b>Value Extraction:</b> Scalar JSON values (string, number, boolean, null) are converted to
/// their CLR equivalents by <see cref="GetValue"/>. JSON objects and arrays are returned as a raw
/// <see cref="JsonElement"/> — no unwrapping is performed.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of rows in the query result. Must be <see cref="JsonElement"/>.</typeparam>
public class CouchbaseDbDataReader<T> : DbDataReader
{
    private readonly IQueryResult<T> _queryResult;
    private readonly DbConnection? _connection;
    private readonly CommandBehavior _behavior;
    // Ordered SELECT projection aliases supplied by the caller (one per shaper ordinal).
    private readonly string?[]? _columnNames;
    // Reverse map of _columnNames: alias (case-insensitive) → projection ordinal.
    private readonly Dictionary<string, int>? _projectionOrdinals;
    private IAsyncEnumerator<T>? _enumerator;
    private CancellationToken _cancellationToken;
    // Owned linked CTS transferred from CouchbaseCommand so Cancel() propagates for the reader's lifetime.
    private CancellationTokenSource? _linkedCts;
    private T? _currentRow;
    // First-row buffer populated by PrimeAsync so HasRows is accurate before ReadAsync.
    private T? _bufferedFirstRow;
    private bool _hasBufferedRow;

    /// <summary>
    /// Gets the current row as read by the last <see cref="ReadAsync"/> call.
    /// </summary>
    public T? CurrentRow => _currentRow;
    private bool _hasCurrentRow;
    private bool? _hasRows;
    private bool _isClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseDbDataReader{T}"/> class with an
    /// optional column-name mapping. When <paramref name="columnNames"/> is non-null it must
    /// contain the SELECT-clause alias for each EF Core <c>readerColumn</c> in order; <c>null</c>
    /// elements within the array use positional mapping (resolved from the current row after
    /// <see cref="ReadAsync"/>).  When <paramref name="columnNames"/> itself is <c>null</c> the
    /// reader operates in the raw positional path (same behaviour as the 4-argument constructor).
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryResult"/> is null.</exception>
    public CouchbaseDbDataReader(IQueryResult<T> queryResult, string?[]? columnNames)
        : this(queryResult, null, CommandBehavior.Default, CancellationToken.None)
    {
        if (columnNames != null)
        {
            _columnNames = columnNames;
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
    /// Initializes a new instance of the <see cref="CouchbaseDbDataReader{T}"/> class with
    /// connection and behavior options (raw ADO.NET path — no column-name mapping).
    /// </summary>
    /// <param name="queryResult">The Couchbase query result to read from.</param>
    /// <param name="connection">The connection to close when <see cref="CommandBehavior.CloseConnection"/> is set.</param>
    /// <param name="behavior">The command behavior flags.</param>
    /// <param name="cancellationToken">Unused at construction; reserved for future use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryResult"/> is null.</exception>
    public CouchbaseDbDataReader(IQueryResult<T> queryResult, DbConnection? connection, CommandBehavior behavior, CancellationToken cancellationToken)
    {
        _queryResult = queryResult ?? throw new ArgumentNullException(nameof(queryResult));
        _connection = connection;
        _behavior = behavior;
    }

    /// <summary>
    /// Gets the number of fields in the current row.
    /// </summary>
    /// <remarks>
    /// When column names were supplied at construction this returns the projection length and
    /// does not require a prior <see cref="Read"/> call. Otherwise a current row is required.
    /// </remarks>
    public override int FieldCount
    {
        get
        {
            if (_columnNames != null)
                return _columnNames.Length;
            EnsureCurrentRow();
            if (_currentRow is JsonElement je)
                return je.ValueKind == JsonValueKind.Object ? je.EnumerateObject().Count() : 1;
            return 0;
        }
    }

    /// <summary>
    /// Gets the number of rows changed, inserted, or deleted. Always returns -1 for Couchbase queries.
    /// </summary>
    public override int RecordsAffected => -1;

    /// <summary>
    /// Gets a value indicating whether the result set contains one or more rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Primed path (<see cref="CouchbaseCommand"/>):</b> <see cref="CouchbaseCommand"/>
    /// calls <see cref="PrimeAsync"/> immediately after constructing the reader, which advances
    /// to the first row and sets this property before the reader is returned to the caller.
    /// <c>HasRows</c> is therefore accurate before the first <see cref="Read"/> call.
    /// </para>
    /// <para>
    /// <b>Unprimed path (direct construction):</b> When the reader is constructed without a
    /// subsequent <see cref="PrimeAsync"/> call, <c>HasRows</c> returns <c>false</c> until the
    /// first <see cref="ReadAsync"/> call populates it.
    /// </para>
    /// Once set, the value does not change.
    /// </remarks>
    public override bool HasRows => _hasRows ?? false;

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
    public override object this[int ordinal] => GetValue(ordinal);

    /// <summary>
    /// Gets the value of the specified column by name.
    /// </summary>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <summary>
    /// Advances the reader to the next row synchronously.
    /// </summary>
    public override bool Read()
    {
        return ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously advances the reader to the next row.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns><c>true</c> if there are more rows; otherwise, <c>false</c>.</returns>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (_isClosed)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        // If PrimeAsync buffered the first row, return it without re-advancing the enumerator.
        if (_hasBufferedRow)
        {
            _currentRow = _bufferedFirstRow!;
            _bufferedFirstRow = default;
            _hasBufferedRow = false;
            _hasCurrentRow = true;
            return true;
        }

        EnsureEnumerator(cancellationToken);

        var hasMore = await _enumerator!.MoveNextAsync().ConfigureAwait(false);
        if (hasMore)
        {
            _currentRow = ValidateRow(_enumerator.Current);
            _hasCurrentRow = true;
            _hasRows ??= true;
        }
        else
        {
            _currentRow = default;
            _hasCurrentRow = false;
            _hasRows ??= false;
        }

        return hasMore;
    }

    private void EnsureEnumerator(CancellationToken cancellationToken)
    {
        if (_enumerator == null)
        {
            _cancellationToken = cancellationToken;
            _enumerator = _queryResult.Rows.GetAsyncEnumerator(_cancellationToken);
        }
    }

    /// <summary>
    /// Asynchronously advances to the first row and buffers it so that <see cref="HasRows"/>
    /// returns the correct value before the first <see cref="ReadAsync"/> call, satisfying the
    /// ADO.NET <see cref="DbDataReader.HasRows"/> contract.  Called by <see cref="CouchbaseCommand"/>
    /// immediately after constructing the reader.
    /// </summary>
    internal async Task PrimeAsync(CancellationToken cancellationToken)
    {
        if (_hasRows.HasValue || _hasBufferedRow || _hasCurrentRow)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEnumerator(cancellationToken);
        var hasMore = await _enumerator!.MoveNextAsync().ConfigureAwait(false);
        if (hasMore)
        {
            _bufferedFirstRow = ValidateRow(_enumerator.Current);
            _hasBufferedRow = true;
            _hasRows = true;
        }
        else
        {
            _hasRows = false;
        }
    }

    /// <summary>
    /// Transfers ownership of the linked <see cref="CancellationTokenSource"/> created by
    /// <see cref="CouchbaseCommand"/> so that <c>DbCommand.Cancel()</c> propagates to the
    /// enumerator for the reader's full lifetime.  The source is disposed when the reader closes.
    /// </summary>
    internal void SetLinkedCts(CancellationTokenSource cts) => _linkedCts = cts;

    /// <summary>
    /// Advances the reader to the next result set. Always returns <c>false</c> for Couchbase.
    /// </summary>
    public override bool NextResult() => false;

    /// <summary>
    /// Asynchronously advances the reader to the next result set. Always returns <c>false</c> for Couchbase.
    /// </summary>
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>
    /// Closes the reader and releases resources.
    /// </summary>
    public override void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _enumerator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _linkedCts?.Dispose();
            _linkedCts = null;

            if ((_behavior & CommandBehavior.CloseConnection) != 0 && _connection != null)
                _connection.Close();
        }
    }

    /// <summary>
    /// Asynchronously closes the reader and releases resources.
    /// </summary>
    public override async Task CloseAsync()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            if (_enumerator != null)
                await _enumerator.DisposeAsync().ConfigureAwait(false);
            _linkedCts?.Dispose();
            _linkedCts = null;

            if ((_behavior & CommandBehavior.CloseConnection) != 0 && _connection != null)
                await _connection.CloseAsync().ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();
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
    /// When a non-null column alias exists for the ordinal it is returned immediately. For null-slot
    /// positions, or when no column names were supplied, the JSON field name is read from the current
    /// row and a prior <see cref="Read"/> call is required.
    /// </remarks>
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
            // null slot: positional resolution requires a current row
            EnsureCurrentRow();
            if (_currentRow is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                var i = 0;
                foreach (var prop in je.EnumerateObject())
                    if (i++ == ordinal) return prop.Name;
            }
            throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range. FieldCount: {_columnNames.Length}");
        }

        // No column names: positional from current row.
        EnsureCurrentRow();
        if (_currentRow is JsonElement jePos)
        {
            if (jePos.ValueKind == JsonValueKind.Object)
            {
                var i = 0;
                foreach (var prop in jePos.EnumerateObject())
                    if (i++ == ordinal) return prop.Name;
                throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range.");
            }
            if (ordinal == 0)
                return string.Empty;
        }
        throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range.");
    }

    /// <summary>
    /// Gets the ordinal of the field with the specified name.
    /// </summary>
    /// <remarks>
    /// Field name lookup is case-insensitive. When column names were supplied at construction,
    /// non-null aliases resolve via an O(1) dictionary without requiring a prior <see cref="Read"/>
    /// call. Null-slot names and the no-column-names path require a current row.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown when the field is not found.</exception>
    public override int GetOrdinal(string name)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        // Non-null projection aliases resolve immediately.
        if (_projectionOrdinals != null && _projectionOrdinals.TryGetValue(name, out var projOrdinal))
            return projOrdinal;

        if (_columnNames != null)
        {
            // Null-slot fallback: positional resolution requires a current row.
            EnsureCurrentRow();
            if (_currentRow is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                var i = 0;
                foreach (var prop in je.EnumerateObject())
                {
                    if ((uint)i < (uint)_columnNames.Length
                        && _columnNames[i] == null
                        && StringComparer.OrdinalIgnoreCase.Equals(prop.Name, name))
                        return i;
                    i++;
                }
            }
            throw new IndexOutOfRangeException($"Field '{name}' not found.");
        }

        // No column names: positional scan of current row.
        EnsureCurrentRow();
        if (_currentRow is JsonElement jePos)
        {
            if (jePos.ValueKind == JsonValueKind.Object)
            {
                var i = 0;
                foreach (var prop in jePos.EnumerateObject())
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(prop.Name, name))
                        return i;
                    i++;
                }
            }
            else
            {
                // Scalar SELECT RAW: any name maps to ordinal 0.
                return 0;
            }
        }
        throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }

    /// <summary>
    /// Gets the data type name of the field at the specified ordinal.
    /// </summary>
    public override string GetDataTypeName(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType().Name ?? "Object";
    }

    /// <summary>
    /// Gets the CLR type of the field at the specified ordinal.
    /// </summary>
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
    /// JSON objects and arrays are returned as a raw <see cref="JsonElement"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no current row exists (call <see cref="Read"/> first).</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="ordinal"/> is out of range.</exception>
    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();

        if (_columnNames != null)
        {
            if ((uint)ordinal >= (uint)_columnNames.Length)
                throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range. FieldCount: {_columnNames.Length}");

            var colName = _columnNames[ordinal];
            if (colName != null)
            {
                if (_currentRow is not JsonElement je) return DBNull.Value;
                if (je.ValueKind != JsonValueKind.Object)
                    return ConvertJsonElement(je); // SELECT RAW scalar
                return je.TryGetPropertyCI(colName, out var prop)
                    ? ConvertJsonElement(prop)
                    : DBNull.Value;
            }

            // null slot: positional access from current row
            if (_currentRow is JsonElement jePos)
            {
                if (jePos.ValueKind == JsonValueKind.Object)
                {
                    var i = 0;
                    foreach (var p in jePos.EnumerateObject())
                    {
                        if (i == ordinal) return ConvertJsonElement(p.Value);
                        i++;
                    }
                }
                else if (ordinal == 0)
                    return ConvertJsonElement(jePos); // scalar SELECT RAW at null slot 0
            }
            return DBNull.Value;
        }

        // No column names: positional access from current row.
        if (_currentRow is JsonElement jeFallback)
        {
            if (jeFallback.ValueKind == JsonValueKind.Object)
            {
                var i = 0;
                foreach (var p in jeFallback.EnumerateObject())
                {
                    if (i == ordinal) return ConvertJsonElement(p.Value);
                    i++;
                }
                throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range.");
            }
            if (ordinal == 0)
                return ConvertJsonElement(jeFallback);
        }
        throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range.");
    }

    /// <summary>
    /// Populates an array with the values of all fields in the current row.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="values"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no current row exists.</exception>
    public override int GetValues(object[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        EnsureCurrentRow();

        int fieldCount;
        if (_columnNames != null)
        {
            fieldCount = _columnNames.Length;
        }
        else if (_currentRow is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            fieldCount = je.EnumerateObject().Count();
        }
        else
        {
            fieldCount = _currentRow is JsonElement ? 1 : 0;
        }

        var count = Math.Min(values.Length, fieldCount);
        for (var i = 0; i < count; i++)
            values[i] = GetValue(i);
        return count;
    }

    /// <summary>
    /// Determines whether the field at the specified ordinal is null or <see cref="DBNull.Value"/>.
    /// </summary>
    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value == null || value == DBNull.Value;
    }

    /// <summary>
    /// Asynchronously determines whether the field at the specified ordinal is null.
    /// </summary>
    public override async Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        return IsDBNull(ordinal);
    }

    /// <summary>Gets the boolean value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the byte value of the field at the specified ordinal.</summary>
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
    /// The field value must be a byte array or a base64-encoded string. If <paramref name="buffer"/>
    /// is null, returns the total length of the byte data without copying.
    /// </remarks>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(dataOffset), dataOffset, "Data offset cannot be negative.");

        var value = GetValue(ordinal);
        byte[] bytes = value switch
        {
            byte[] b => b,
            string s => Convert.FromBase64String(s),
            JsonElement je when je.ValueKind == JsonValueKind.String => Convert.FromBase64String(je.GetString()!),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name} to byte array.")
        };

        if (buffer == null)
            return bytes.Length;

        if (bufferOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset cannot be negative.");

        if (bufferOffset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset exceeds buffer length.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length cannot be negative.");

        if (dataOffset >= bytes.Length)
            return 0;

        var sourceOffset = (int)dataOffset;
        var bytesToCopy = Math.Min(length, Math.Min(bytes.Length - sourceOffset, buffer.Length - bufferOffset));
        if (bytesToCopy > 0)
            Array.Copy(bytes, sourceOffset, buffer, bufferOffset, bytesToCopy);
        return bytesToCopy;
    }

    /// <summary>Gets the character value of the field at the specified ordinal.</summary>
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
    /// If <paramref name="buffer"/> is null, returns the total length of the string without copying.
    /// </remarks>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(dataOffset), dataOffset, "Data offset cannot be negative.");

        var str = GetString(ordinal);

        if (buffer == null)
            return str.Length;

        if (bufferOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset cannot be negative.");

        if (bufferOffset > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), bufferOffset, "Buffer offset exceeds buffer length.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length cannot be negative.");

        if (dataOffset >= str.Length)
            return 0;

        var sourceOffset = (int)dataOffset;
        var charsToCopy = Math.Min(length, Math.Min(str.Length - sourceOffset, buffer.Length - bufferOffset));
        if (charsToCopy > 0)
            str.CopyTo(sourceOffset, buffer, bufferOffset, charsToCopy);
        return charsToCopy;
    }

    /// <summary>Gets the DateTime value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the decimal value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the double value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the float value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the GUID value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the 16-bit signed integer value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the 32-bit signed integer value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the 64-bit signed integer value of the field at the specified ordinal.</summary>
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

    /// <summary>Gets the string value of the field at the specified ordinal.</summary>
    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()!,
            JsonElement je => je.GetRawText(),
            // EF Core materializer lambdas call GetString without IsDBNull for non-nullable
            // string properties on optional columns; returning null lets those round-trips work.
            DBNull => null!,
            null => throw new InvalidCastException("Cannot convert null to string."),
            _ => value.ToString()!
        };
    }

    /// <summary>Gets the value of the field at the specified ordinal as the specified type.</summary>
    public override T1 GetFieldValue<T1>(int ordinal)
    {
        var value = GetValue(ordinal);

        if (value is T1 typedValue)
            return typedValue;

        if (value is JsonElement je)
            return je.Deserialize<T1>()!;

        return (T1)Convert.ChangeType(value, typeof(T1))!;
    }

    /// <summary>
    /// Returns a <see cref="DataTable"/> that describes the column metadata of the result set.
    /// </summary>
    /// <remarks>
    /// When column names were supplied at construction they are used directly. Otherwise the
    /// schema is derived from the current row and a prior <see cref="Read"/> call is required.
    /// All columns are reported as <see cref="object"/> type with <c>AllowDBNull = true</c>.
    /// </remarks>
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

        // No column names: build from current row.
        EnsureCurrentRow();
        if (_currentRow is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var i = 0;
            foreach (var prop in je.EnumerateObject())
            {
                var row = schemaTable.NewRow();
                row["ColumnName"] = prop.Name;
                row["ColumnOrdinal"] = i++;
                row["DataType"] = typeof(object);
                row["AllowDBNull"] = true;
                schemaTable.Rows.Add(row);
            }
        }
        return schemaTable;
    }

    /// <summary>Returns an enumerator that iterates through the rows of the result set.</summary>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    private void EnsureCurrentRow()
    {
        if (!_hasCurrentRow)
            throw new InvalidOperationException("No current row. Call Read() first.");
    }

    private static T ValidateRow(T row)
    {
        if (row is not null and not JsonElement)
            throw new NotSupportedException(
                $"Row type '{row.GetType().Name}' is not supported. " +
                "The query must be executed with cluster.QueryAsync<JsonElement>().");
        return row;
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
