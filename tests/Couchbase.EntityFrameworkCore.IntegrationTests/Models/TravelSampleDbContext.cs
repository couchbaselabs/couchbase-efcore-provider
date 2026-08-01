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

        // Geo is a single embedded object, not an array. OwnsOne's own flat "geo_Lat"/"geo_Lon"
        // table-split columns never match travel-sample's genuinely nested geo object
        // ({"geo":{"lat":...}}), but CouchbaseOwnedCollectionMaterializer.PopulateReference
        // overrides those flat-column reads with the nested JSON when present.
        modelBuilder.Entity<TravelSampleFixture.Hotel>().OwnsOne(h => h.Geo);

        // PublicLikes is a scalar primitive collection (List<string>, not OwnsMany) -- its
        // [JsonPropertyName("public_likes")] attribute is currently NOT honored for primitive-
        // collection properties (a separate, pre-existing bug: JsonPropertyNameConvention's
        // HasColumnName gets overwritten by something in the primitive-collection discovery path,
        // confirmed empirically -- the generated column name comes out "publicLikes", not
        // "public_likes"). Set the column name explicitly via fluent API until that's fixed.
        modelBuilder.Entity<TravelSampleFixture.Hotel>().Property(h => h.PublicLikes).HasColumnName("public_likes");

        // Configure Couchbase mappings
        modelBuilder.ConfigureToCouchbase(this, true);
        modelBuilder.Entity<TravelSampleFixture.DestinationAirport>().ToCouchbaseCollection(this, "destinationairport");
    }
}