using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Infrastructure;

public class CouchbaseDbContextOptionsBuilder : ICouchbaseDbContextOptionsBuilder
{
    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, ClusterOptions clusterOptions)
    {
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = clusterOptions;
    }

    private DbContextOptionsBuilder OptionsBuilder { get; }
    
    public ClusterOptions ClusterOptions { get; }

    DbContextOptionsBuilder ICouchbaseDbContextOptionsBuilder.OptionsBuilder => OptionsBuilder;
}

public interface ICouchbaseDbContextOptionsBuilder
{
    DbContextOptionsBuilder OptionsBuilder { get; }
    
    ClusterOptions ClusterOptions { get; }
}