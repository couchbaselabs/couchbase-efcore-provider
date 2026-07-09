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

        // Reviews/Addresses are embedded arrays, which OwnsMany reads via the provider's
        // JSON-based owned-collection materializer, so they round-trip correctly.
        modelBuilder.Entity<TravelSampleFixture.User>().OwnsMany(u => u.Addresses);
        modelBuilder.Entity<TravelSampleFixture.Hotel>().OwnsMany(h => h.Reviews, r => r.OwnsOne(rv => rv.Ratings));

        // Geo is a single embedded object, not an array. OwnsOne maps it via EF Core's relational
        // table-splitting, which projects flat "geo_Lat"/"geo_Lon" columns — but travel-sample
        // stores geo as a genuinely nested JSON object ({"geo":{"lat":...}}), so those columns
        // never match and the properties always read back null. See Test_Hotel_With_Geo's skip
        // reason for details; ignore until OwnsOne gets nested-path read support.
        modelBuilder.Entity<TravelSampleFixture.Hotel>().Ignore(h => h.Geo);

        // Configure Couchbase mappings
        modelBuilder.ConfigureToCouchbase(this, true);
        modelBuilder.Entity<TravelSampleFixture.DestinationAirport>().ToCouchbaseCollection(this, "destinationairport");
    }
}