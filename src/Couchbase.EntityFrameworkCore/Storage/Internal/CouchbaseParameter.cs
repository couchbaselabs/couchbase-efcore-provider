using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseParameter : DbParameter
{
    private string _parameterName = string.Empty;
    private object? _value;

    public CouchbaseParameter()
    {
    }

    public CouchbaseParameter(string name, object? value)
    {
        _value = value;
        _parameterName = name;
    }

    public override void ResetDbType()
    {
        throw new NotImplementedException();
    }

    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }

    [AllowNull]
    public override string ParameterName
    {
        get => _parameterName;
        set => _parameterName = value ?? string.Empty;
    }

    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    public override object? Value
    {
        get => _value;
        set => _value = value;
    }

    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
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
