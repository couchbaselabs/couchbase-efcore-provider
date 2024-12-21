using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Infrastructure;

public class CouchbaseDbContextOptionsBuilder : ICouchbaseDbContextOptionsBuilder
{
    private readonly string _connectionString;

    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, string connectionString)
    {
        _connectionString = connectionString;
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = new ClusterOptions().WithConnectionString(connectionString);
    }
    
    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, ClusterOptions clusterOptions)
    {
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = clusterOptions;
    }

    private DbContextOptionsBuilder OptionsBuilder { get; }

    public ClusterOptions ClusterOptions { get; }

    public string Bucket { get; set; }

    public string Scope { get; set; }
    
    public string ConnectionString => ClusterOptions.ConnectionString!;

    DbContextOptionsBuilder ICouchbaseDbContextOptionsBuilder.OptionsBuilder => OptionsBuilder;
}

public interface ICouchbaseDbContextOptionsBuilder
{
    DbContextOptionsBuilder OptionsBuilder { get; }

    ClusterOptions ClusterOptions { get; }

    public string Bucket { get; }

    public string Scope { get; }
    string ConnectionString { get; }
}