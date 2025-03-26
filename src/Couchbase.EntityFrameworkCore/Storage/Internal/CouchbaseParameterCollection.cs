using System.Collections;
using System.Data.Common;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseParameterCollection : DbParameterCollection
{
    private readonly List<CouchbaseParameter> _parameters = new();

    public virtual CouchbaseParameter AddWithValue(string? parameterName, object? value)
        => Add(new CouchbaseParameter(parameterName, value));

    public virtual CouchbaseParameter Add(CouchbaseParameter value)
    {
        _parameters.Add(value);

        return value;
    }

    public override int Add(object value)
    {
        var index = _parameters.Count;
        _parameters.Insert(index, (CouchbaseParameter)value);
        return index;
    }

    public override void Clear()
    {
        _parameters.Clear();
    }

    public override bool Contains(object value)
    {
        return IndexOf(value) != -1;
    }

    /// <summary>Indicates whether a <see cref="T:System.Data.Common.DbParameter" /> with the specified name exists in the collection.</summary>
    /// <param name="value">The name of the <see cref="T:System.Data.Common.DbParameter" /> to look for in the collection.</param>
    /// <returns>
    /// <see langword="true" /> if the <see cref="T:System.Data.Common.DbParameter" /> is in the collection; otherwise <see langword="false" />.</returns>
    public override bool Contains(string value)
    {
       return IndexOf(value) != -1;
    }

    public override int IndexOf(object value)
    {
        return _parameters.IndexOf((CouchbaseParameter)value);
    }

    public override void Insert(int index, object value)
    {
        throw new NotImplementedException();
    }

    public override void Remove(object value)
    {
        throw new NotImplementedException();
    }

    public override void RemoveAt(int index)
    {
        throw new NotImplementedException();
    }

    public override void RemoveAt(string parameterName)
    {
        throw new NotImplementedException();
    }

    protected override void SetParameter(int index, DbParameter value)
    {
        throw new NotImplementedException();
    }

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        throw new NotImplementedException();
    }

    public override int Count { get; }
    public override object SyncRoot { get; }

    public override int IndexOf(string parameterName)
    {
        for (var index = 0; index < _parameters.Count; index++)
        {
            if (_parameters[index].ParameterName == parameterName)
            {
                return index;
            }
        }

        return -1;
    }

    public override void CopyTo(Array array, int index)
    {
        throw new NotImplementedException();
    }

    public override IEnumerator GetEnumerator()
    {
        return _parameters.GetEnumerator();
    }

    protected override DbParameter GetParameter(int index)
    {
        throw new NotImplementedException();
    }

    protected override DbParameter GetParameter(string parameterName)
    {
        throw new NotImplementedException();
    }

    public override void AddRange(Array values)
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
