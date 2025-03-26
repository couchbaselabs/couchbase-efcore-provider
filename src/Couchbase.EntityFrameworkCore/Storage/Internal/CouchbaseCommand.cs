using System.Data;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseCommand : DbCommand
{
    public CouchbaseCommand(){}

    private CouchbaseParameterCollection? _parameters;
    public new virtual CouchbaseParameterCollection Parameters
        => _parameters ??= new CouchbaseParameterCollection();

    internal ICluster Cluster { get; set; }

    public override void Cancel()
    {
        throw new NotImplementedException();
    }

    public override int ExecuteNonQuery()
    {
        var result = Cluster.QueryAsync<int>(CommandText).GetAwaiter().GetResult();
        return result.Rows.CountAsync().GetAwaiter().GetResult();
    }

    public override object? ExecuteScalar()
    {
        throw new NotImplementedException();
    }

    public override void Prepare()
    {
        throw new NotImplementedException();
    }

    public override string CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => Parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override bool DesignTimeVisible { get; set; }

    protected override DbParameter CreateDbParameter()
    {
        return new CouchbaseParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var options = new QueryOptions();
        foreach (var dbParameter in Parameters)
        {
            var parameter = (CouchbaseParameter)dbParameter;
            options.Parameter(parameter.ParameterName, parameter.Value);
        }

        var queryResult = Cluster.QueryAsync<object>(CommandText, options).GetAwaiter().GetResult();
        return new CouchbaseDbDataReader<object>(queryResult);
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
