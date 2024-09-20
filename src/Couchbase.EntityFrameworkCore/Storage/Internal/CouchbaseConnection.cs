using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection :  DbConnection
{
    private readonly IClusterProvider _clusterProvider;
    private static ConcurrentDictionary<string, ICluster> _clusters = new();
    private string _connectionString;
    private readonly ClusterOptions _clusterOptions;

    public CouchbaseConnection(string connectionString, ClusterOptions clusterOptions, IClusterProvider clusterProvider)
    {
        _connectionString = connectionString;
        _clusterOptions = clusterOptions;
        _clusterProvider = clusterProvider;
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
        if (_clusters.ContainsKey(ConnectionString))
        {
            return;
        }

        var cluster = _clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        _clusters.TryAdd(ConnectionString, cluster);
    }

    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value;
    }

    public override string Database { get; }
    public override ConnectionState State { get; }
    public override string DataSource { get; }
    public override string ServerVersion { get; }

    protected override DbCommand CreateDbCommand()
    {
        var cluster = _clusters.GetOrAdd(ConnectionString, s => _clusterProvider.GetClusterAsync().GetAwaiter().GetResult());
        return new CouchbaseCommand
        {
            Connection = this,
            Cluster = cluster
        };
    }
}