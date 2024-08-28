using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection :  DbConnection
{
    private static Dictionary<string, ICluster> _clusters = new();
    private string _connectionString;
    private readonly ClusterOptions _clusterOptions;

    public CouchbaseConnection(string connectionString, ClusterOptions clusterOptions)
    {
        _connectionString = connectionString;
        _clusterOptions = clusterOptions;
    }
    
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotImplementedException();
    }

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException();
    }

    public override void Close()
    {
    }

    public override void Open()
    {
        if (_clusters.ContainsKey(_connectionString))
        {
            return;
        }

        var cluster = Cluster.ConnectAsync(_clusterOptions).GetAwaiter().GetResult();
        _clusters.Add(_connectionString, cluster);
    }

    public override string ConnectionString { get; set; }
    public override string Database { get; }
    public override ConnectionState State { get; }
    public override string DataSource { get; }
    public override string ServerVersion { get; }

    protected override DbCommand CreateDbCommand()
        => new CouchbaseCommand
        {
            Connection = this,
         //   CommandTimeout = DefaultTimeout,
          //  Transaction = Transaction
        };
}