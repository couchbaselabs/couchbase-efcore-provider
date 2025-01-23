using ContosoUniversity.Data;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;

public class ContosoContext : SchoolContext
{
    private ClusterOptions _clusterOptions;
    
    public ContosoContext(ClusterOptions clusterOptions) : base(new DbContextOptions<SchoolContext>())
    {
        _clusterOptions = clusterOptions;
    }
    public ContosoContext(DbContextOptions<SchoolContext> options, ClusterOptions clusterOptions) : base(options)
    {
        _clusterOptions = clusterOptions;
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });

        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseLoggerFactory(loggerFactory);
        optionsBuilder.UseCouchbase(_clusterOptions,
            couchbaseDbContextOptions =>
        {
            couchbaseDbContextOptions.Bucket = "universities";
            couchbaseDbContextOptions.Scope = "contoso";
        });
        optionsBuilder.EnableSensitiveDataLogging();
        optionsBuilder.EnableDetailedErrors();
    }
}