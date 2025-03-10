using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection :  DbConnection
{
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;

    public CouchbaseConnection(IBucketProvider bucketProvider, ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder, string database, string dataSource, string serverVersion)
    {
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        Database = database;
        DataSource = dataSource;
        ServerVersion = serverVersion;
    }

    public CouchbaseConnection(IBucketProvider bucketProvider, ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _bucketProvider = bucketProvider;
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
      //noop
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
        //This sync over async needs to be fixed
        var bucket = _bucketProvider.
            GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).GetAwaiter().GetResult();
        return new CouchbaseCommand
        {
            Connection = this,
            Cluster = bucket.Cluster
        };
    }
}