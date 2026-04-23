using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Models;

public class TravelSampleDbContext(DbContextOptions<TravelSampleDbContext> options) : DbContext(options)
{
    public DbSet<TravelSampleFixture.Airline> Airlines { get; set; }

    public DbSet<TravelSampleFixture.User> Users { get; set; }
    
    public DbSet<TravelSampleFixture.Address> Address { get; set; }
        
    public DbSet<TravelSampleFixture.DestinationAirport> DestinationAirport { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureToCouchbase(this, true);
        modelBuilder.Entity<TravelSampleFixture.Airline>().HasKey(x=>new {x.Type, x.Id});//composite key mapping
        modelBuilder.Entity<TravelSampleFixture.DestinationAirport>().ToCouchbaseCollection(this, "destinationairport");
        modelBuilder.Entity<TravelSampleFixture.DestinationAirport>().HasNoKey();
    }
}