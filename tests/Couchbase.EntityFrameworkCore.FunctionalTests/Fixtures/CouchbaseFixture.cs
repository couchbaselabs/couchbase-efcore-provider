using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Models;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EFCore.NamingConventions;
using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public class CouchbaseFixture : IAsyncLifetime
{
    public TravelSampleDbContext? GetDbContext { get; private set; }
    
    public Task InitializeAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });

        var options = new ClusterOptions()
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory)
            .WithBuckets("travel-sample");

        GetDbContext = new TravelSampleDbContext(options);
        return Task.CompletedTask;
    }

    public class TravelSampleDbContext : DbContext
    {
        private readonly ClusterOptions _clusterOptions;

        public TravelSampleDbContext(ClusterOptions clusterOptions)
        {
            _clusterOptions = clusterOptions;
        }

        public DbSet<Airline> Airlines { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter(level => level >= LogLevel.Debug);
                builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
            });

            base.OnConfiguring(optionsBuilder);
            optionsBuilder.UseLoggerFactory(loggerFactory);
            optionsBuilder.UseCouchbase(_clusterOptions);
            optionsBuilder.UseCamelCaseNamingConvention();
        }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Airline>().HasKey(x=>new {x.Type, x.Id});//composite key mapping
            modelBuilder.Entity<User>().ToCouchbaseCollection("users", "users");
        }

        private interface ITravelSampleBucketProvider : INamedBucketProvider;
    }

    public async Task DisposeAsync()
    {
       //await GetDbContext.DisposeAsync();
    }
}