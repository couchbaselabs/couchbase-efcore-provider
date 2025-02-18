using System.Data;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
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
       var queryResult = Cluster.QueryAsync<object>(CommandText).GetAwaiter().GetResult();
        //throw new NotImplementedException();
        return new CouchbaseDbDataReader<object>(queryResult);
    }
}