using System.Collections;
using System.Data.Common;
using Couchbase.Core.IO.Serializers;
using Couchbase.Query;
using System.Threading.Tasks;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDbDataReader<T> : DbDataReader
{
    private readonly IQueryResult<T> _queryResult;
    private readonly IAsyncEnumerator<T> _enumerator;
    private bool _read;

    public CouchbaseDbDataReader(IQueryResult<T> queryResult)
    {
        _queryResult = queryResult;
        _enumerator = _queryResult.GetAsyncEnumerator(CancellationToken.None);
    }

    public override bool GetBoolean(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override byte GetByte(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
    }

    public override char GetChar(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        throw new NotImplementedException();
    }

    public override string GetDataTypeName(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override DateTime GetDateTime(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override decimal GetDecimal(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override double GetDouble(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override Type GetFieldType(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override float GetFloat(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override Guid GetGuid(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override short GetInt16(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int GetInt32(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override long GetInt64(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override string GetName(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int GetOrdinal(string name)
    {
        throw new NotImplementedException();
    }

    public override string GetString(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override object GetValue(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int GetValues(object[] values)
    {
        throw new NotImplementedException();
    }

    public override bool IsDBNull(int ordinal)
    {
        throw new NotImplementedException();
    }

    public override int FieldCount { get; }

    public override object this[int ordinal] => throw new NotImplementedException();

    public override object this[string name] => throw new NotImplementedException();

    public override int RecordsAffected { get; }

    public override bool HasRows
        => _queryResult.MetaData.Status == QueryStatus.Success; //temporary

    public override bool IsClosed { get; }

    public override bool NextResult()
    {
        throw new NotImplementedException();
    }

    public override bool Read()
    {
        try
        {
            if (_read)
            {
                return false;
            }
            var moreRows =_enumerator.MoveNextAsync().GetAwaiter().GetResult();
            if (moreRows == false)
            {
                _read = true;
                return moreRows;
            }

            return moreRows;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public override int Depth { get; }

    public override IEnumerator GetEnumerator()
    {
        throw new NotImplementedException();
    }
}