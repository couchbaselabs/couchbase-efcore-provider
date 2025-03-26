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
            //TODO: should be awaited - CA2012 - Use ValueTasks correctly
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
