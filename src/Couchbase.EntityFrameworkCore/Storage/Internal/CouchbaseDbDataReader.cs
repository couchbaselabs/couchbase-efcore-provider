using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Couchbase.Query;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDbDataReader<T> : DbDataReader
{
    private readonly IQueryResult<T> _queryResult;
    private IAsyncEnumerator<T>? _enumerator;
    private CancellationToken _cancellationToken;
    private T? _currentRow;
    private T? _bufferedRow;
    private bool _hasBufferedRow;
    private bool _hasCurrentRow;
    private List<string>? _fieldNames;
    private Dictionary<string, int>? _fieldOrdinals;
    private bool _isClosed;
    private bool _schemaInitialized;

    public CouchbaseDbDataReader(IQueryResult<T> queryResult)
    {
        _queryResult = queryResult ?? throw new ArgumentNullException(nameof(queryResult));
    }

    public override int FieldCount
    {
        get
        {
            EnsureFieldInfo();
            return _fieldNames?.Count ?? 0;
        }
    }

    public override int RecordsAffected => -1;

    public override bool HasRows => _queryResult.MetaData?.Status == QueryStatus.Success;

    public override bool IsClosed => _isClosed;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        return ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

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

    public override bool NextResult()
    {
        // Couchbase queries return a single result set
        return false;
    }

    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        return false;
    }

    public override void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _enumerator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public override async Task CloseAsync()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            if (_enumerator != null)
            {
                await _enumerator.DisposeAsync().ConfigureAwait(false);
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

    public override string GetName(int ordinal)
    {
        EnsureFieldInfo();
        ValidateOrdinal(ordinal);
        return _fieldNames![ordinal];
    }

    public override int GetOrdinal(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        EnsureFieldInfo();
        if (_fieldOrdinals != null && _fieldOrdinals.TryGetValue(name, out var ordinal))
        {
            return ordinal;
        }

        throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }

    public override string GetDataTypeName(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType().Name ?? "Object";
    }

    public override Type GetFieldType(int ordinal)
    {
        var value = GetValue(ordinal);
        return value?.GetType() ?? typeof(object);
    }

    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var fieldName = _fieldNames![ordinal];
        return GetFieldValue(fieldName);
    }

    public override int GetValues(object[] values)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        EnsureCurrentRow();
        EnsureFieldInfo();

        var count = Math.Min(values.Length, _fieldNames!.Count);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value == null || value == DBNull.Value;
    }

    public override async Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        return IsDBNull(ordinal);
    }

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

    public override byte GetByte(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            byte b => b,
            int i => (byte)i,
            long l => (byte)l,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetByte(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Byte."),
            _ => Convert.ToByte(value)
        };
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var value = GetValue(ordinal);
        byte[] bytes = value switch
        {
            byte[] b => b,
            string s => Convert.FromBase64String(s),
            JsonElement je when je.ValueKind == JsonValueKind.String => Convert.FromBase64String(je.GetString()!),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType().Name} to byte array.")
        };

        if (buffer == null)
        {
            return bytes.Length;
        }

        var bytesToCopy = Math.Min(length, bytes.Length - (int)dataOffset);
        Array.Copy(bytes, dataOffset, buffer, bufferOffset, bytesToCopy);
        return bytesToCopy;
    }

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

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var str = GetString(ordinal);

        if (buffer == null)
        {
            return str.Length;
        }

        var charsToCopy = Math.Min(length, str.Length - (int)dataOffset);
        str.CopyTo((int)dataOffset, buffer, bufferOffset, charsToCopy);
        return charsToCopy;
    }

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

    public override decimal GetDecimal(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            long l => l,
            int i => i,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDecimal(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Decimal."),
            _ => Convert.ToDecimal(value)
        };
    }

    public override double GetDouble(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            double d => d,
            long l => l,
            int i => i,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Double."),
            _ => Convert.ToDouble(value)
        };
    }

    public override float GetFloat(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            float f => f,
            double d => (float)d,
            long l => l,
            int i => i,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetSingle(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Single."),
            _ => Convert.ToSingle(value)
        };
    }

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

    public override short GetInt16(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            short s => s,
            int i => (short)i,
            long l => (short)l,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt16(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Int16."),
            _ => Convert.ToInt16(value)
        };
    }

    public override int GetInt32(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            int i => i,
            long l => (int)l,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            JsonElement je => throw new InvalidCastException($"Cannot convert JsonElement of kind {je.ValueKind} to Int32."),
            _ => Convert.ToInt32(value)
        };
    }

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

    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()!,
            JsonElement je => je.GetRawText(),
            null => throw new InvalidCastException("Cannot convert null to string."),
            _ => value.ToString()!
        };
    }

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

    public override DataTable GetSchemaTable()
    {
        EnsureFieldInfo();

        var schemaTable = new DataTable("SchemaTable");
        schemaTable.Columns.Add("ColumnName", typeof(string));
        schemaTable.Columns.Add("ColumnOrdinal", typeof(int));
        schemaTable.Columns.Add("DataType", typeof(Type));
        schemaTable.Columns.Add("AllowDBNull", typeof(bool));

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
    /// </summary>
    private void EnsureFieldInfo()
    {
        if (_schemaInitialized || _isClosed)
        {
            return;
        }

        // Create enumerator if needed (with no cancellation for schema discovery)
        EnsureEnumerator(CancellationToken.None);

        // Read the first row to discover schema, but buffer it so it's not lost
        var hasRow = _enumerator!.MoveNextAsync().AsTask().GetAwaiter().GetResult();
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
        else if (_currentRow != null)
        {
            // For non-JsonElement types, use reflection
            var type = _currentRow.GetType();
            var properties = type.GetProperties();
            for (var i = 0; i < properties.Length; i++)
            {
                _fieldNames.Add(properties[i].Name);
                _fieldOrdinals[properties[i].Name] = i;
            }
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
        else if (_currentRow != null)
        {
            var prop = _currentRow.GetType().GetProperty(fieldName);
            if (prop != null)
            {
                return prop.GetValue(_currentRow) ?? DBNull.Value;
            }
        }

        return DBNull.Value;
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.Undefined => DBNull.Value,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Object => ExtractFirstValueFromObject(element),
            JsonValueKind.Array => ExtractFirstValueFromArray(element),
            _ => element
        };
    }

    private static object ExtractFirstValueFromObject(JsonElement element)
    {
        using var enumerator = element.EnumerateObject();
        if (enumerator.MoveNext())
        {
            return ConvertJsonElement(enumerator.Current.Value);
        }
        return DBNull.Value;
    }

    private static object ExtractFirstValueFromArray(JsonElement element)
    {
        using var enumerator = element.EnumerateArray();
        if (enumerator.MoveNext())
        {
            return ConvertJsonElement(enumerator.Current);
        }
        return DBNull.Value;
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
