using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Models;

public class TravelSampleDbContext(DbContextOptions<TravelSampleDbContext> options) : DbContext(options)
{
    public DbSet<TravelSampleFixture.Airline> Airlines { get; set; }

    public DbSet<TravelSampleFixture.User> Users { get; set; }
        
    public DbSet<TravelSampleFixture.DestinationAirport> DestinationAirport { get; set; }
    
    public DbSet<TravelSampleFixture.Hotel> Hotels { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure keys
        modelBuilder.Entity<TravelSampleFixture.Hotel>().HasKey(x => x.Id);
        modelBuilder.Entity<TravelSampleFixture.Airline>().HasKey(x=>new {x.Type, x.Id});
        modelBuilder.Entity<TravelSampleFixture.DestinationAirport>().HasNoKey();

        // Ignore nested object/collection properties - they will be handled by JSON serialization
        // when fetching the full document
        modelBuilder.Entity<TravelSampleFixture.User>().Ignore(u => u.Addresses);
        modelBuilder.Entity<TravelSampleFixture.Hotel>().Ignore(h => h.Geo);
        modelBuilder.Entity<TravelSampleFixture.Hotel>().Ignore(h => h.Reviews);

        // Configure Couchbase mappings
        modelBuilder.ConfigureToCouchbase(this, true);
        modelBuilder.Entity<TravelSampleFixture.DestinationAirport>().ToCouchbaseCollection(this, "destinationairport");
    }
}