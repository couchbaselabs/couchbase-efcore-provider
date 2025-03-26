using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class  CouchbaseRelationalCommandBuilder : IRelationalCommandBuilder
{
    public IReadOnlyList<IRelationalParameter> Parameters { get; }
    public IRelationalCommandBuilder AddParameter(IRelationalParameter parameter)
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder RemoveParameterAt(int index)
    {
        throw new NotImplementedException();
    }

    public IRelationalTypeMappingSource TypeMappingSource { get; }
    public IRelationalCommand Build()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder Append(string value)
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder AppendLine()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder IncrementIndent()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder DecrementIndent()
    {
        throw new NotImplementedException();
    }

    public int CommandTextLength { get; }
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
