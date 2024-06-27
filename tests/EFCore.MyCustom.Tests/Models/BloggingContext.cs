using Couchbase;
using Couchbase.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EFCore.MyCustom.Tests.Models;


public class BloggingContext : DbContext
{
    private readonly ClusterOptions _clusterOptions;

    public BloggingContext(ClusterOptions clusterOptions)
    {
        _clusterOptions = clusterOptions;
    }

    public DbSet<Blog> Blogs { get; set; }
    public DbSet<Post> Posts { get; set; }

    public BloggingContext() : this(new ClusterOptions()
        .WithConnectionString("couchbase://localhost")
        .WithCredentials("Administrator", "password"))
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });
        options.UseLoggerFactory(loggerFactory);
        options.UseCouchbase(_clusterOptions);
    }
}
