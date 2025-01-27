using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection :  DbConnection
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private static ConcurrentDictionary<string, ICluster> _clusters = new();

    public CouchbaseConnection(IServiceProvider serviceProvider, ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _serviceProvider = serviceProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
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

        var clusterProvider = _serviceProvider.GetRequiredKeyedService<IClusterProvider>(_couchbaseDbContextOptionsBuilder.ClusterOptions.ConnectionString);
        var cluster = clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        _clusters.TryAdd(ConnectionString, cluster);
    }

    public override string ConnectionString
    {
        get => _couchbaseDbContextOptionsBuilder.ConnectionString;
        set => throw new NotSupportedException();
    }

    public override string Database { get; }
    public override ConnectionState State { get; }
    public override string DataSource { get; }
    public override string ServerVersion { get; }

    protected override DbCommand CreateDbCommand()
    {
        var cluster = _clusters.GetOrAdd(ConnectionString, s =>
        {
            var clusterProvider = _serviceProvider.GetRequiredKeyedService<IClusterProvider>(_couchbaseDbContextOptionsBuilder.ClusterOptions.ConnectionString);
            return clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        });

        return new CouchbaseCommand
        {
            Connection = this,
            Cluster = cluster
        };
    }
}