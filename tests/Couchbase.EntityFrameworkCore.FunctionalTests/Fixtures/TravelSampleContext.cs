using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Models;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public class TravelSampleDbContext : DbContext
{
    private readonly ClusterOptions _clusterOptions;

    public TravelSampleDbContext(
        DbContextOptions<TravelSampleDbContext> options) : base(options)
    {
    }

    public TravelSampleDbContext(): this(new ClusterOptions()
        .WithConnectionString("http://127.0.0.1")
        .WithCredentials("Administrator", "password"))
    {
    }

    public  TravelSampleDbContext(ClusterOptions clusterOptions)
    {
        _clusterOptions = clusterOptions;
    }

    public DbSet<Airline> Airlines { get; set; }

    public DbSet<User> Users { get; set; }
    
    public DbSet<Address> Address { get; set; }
        
    public DbSet<FromRawSqlTests.DestinationAirport> DestinationAirport { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.UseCouchbase(_clusterOptions,
                couchbaseDbContextOptions =>
                {
                    couchbaseDbContextOptions.Bucket = "travel-sample";
                    couchbaseDbContextOptions.Scope = "inventory";
                });
            optionsBuilder.UseCamelCaseNamingConvention();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureToCouchbase(this, true);
        modelBuilder.Entity<Airline>().HasKey(x=>new {x.Type, x.Id});//composite key mapping
        modelBuilder.Entity<FromRawSqlTests.DestinationAirport>().ToCouchbaseCollection(this, "destinationairport");
        modelBuilder.Entity<FromRawSqlTests.DestinationAirport>().HasNoKey();
    }
}