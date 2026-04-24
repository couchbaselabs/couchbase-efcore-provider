using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Couchbase.EntityFrameworkCode.IntegrationTests.Models;
using Couchbase.EntityFrameworkCore.Metadata;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;

[UsedImplicitly]
public class TravelSampleFixture : CouchbaseFixture<TravelSampleDbContext>
{
    public override string ScopeName { get; } = "inventory";

    public override string BucketName => "travel-sample";

    public override TravelSampleDbContext GetDbContext()
    {
        return new TravelSampleDbContext(CreateDbContextOptions<TravelSampleDbContext>());
    }

    public override Task LoadDataAsync()
    {
        throw new NotImplementedException();
    }
    
    [CouchbaseKeyspace("airline")]
    public class Airline
    {
        [JsonProperty("callsign", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("callsign")]
        public string Callsign { get; set; }
        
        [JsonProperty("country", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonProperty("iata", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("iata")]
        public string Iata { get; set; }

        [JsonProperty("icao", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("icao")]
        public string Icao { get; set; }

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("id")]
        [Key]
        public int Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("type")]
        public string Type { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
    
    public class DestinationAirport
    {
        //public int Id { get; set; }
        
        [JsonPropertyName("destinationairport")]
        public string Destinationairport { get; set; } = string.Empty;
    }

    public class Address
    {
        public Address()
        {
        }

        public Address(string id, string country)
        {
            ID = id;
            Country = country;
        }

        public string ID { get; set; }
        
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonProperty("address", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("address")]
        public string HomeAddress { get; set; }

        [JsonProperty("city", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("city")]
        public string City { get; set; }

        [JsonProperty("country", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("country")]
        public string Country { get; set; }
    }

    public class CreditCard
    {
        public string ID { get; set; }
        
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonProperty("number", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("number")]
        public string Number { get; set; }

        [JsonProperty("expiration", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("expiration")]
        public string Expiration { get; set; }
    }

    [CouchbaseKeyspace("tenant_agent_00", "user")]
    public class User
    {
        public Guid ID { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonProperty("addresses", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("addresses")]
        public List<Address> Addresses { get; set; }

        [JsonProperty("driving_licence", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("driving_licence")]
        public string DrivingLicence { get; set; }

        [JsonProperty("passport", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("passport")]
        public string Passport { get; set; }

        [JsonProperty("preferred_email", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("preferred_email")]
        public string PreferredEmail { get; set; }

        [JsonProperty("preferred_phone", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("preferred_phone")]
        public string PreferredPhone { get; set; }

        [JsonProperty("preferred_airline", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("preferred_airline")]
        public string PreferredAirline { get; set; }

        [JsonProperty("preferred_airport", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("preferred_airport")]
        public string PreferredAirport { get; set; }

        [JsonProperty("credit_cards", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("credit_cards")]
        public List<CreditCard> CreditCards { get; set; }

        [JsonProperty("created", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("created")]
        public string Created { get; set; }

        [JsonProperty("updated", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("updated")]
        public string Updated { get; set; }
    }

    // Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);
    public class Geo
    {
        [JsonPropertyName("lat")]
        public double? Lat { get; set; }

        [JsonPropertyName("lon")]
        public double? Lon { get; set; }

        [JsonPropertyName("accuracy")]
        public string Accuracy { get; set; }
    }

    public class Ratings
    {
        [JsonPropertyName("Service")]
        public int? Service { get; set; }

        [JsonPropertyName("Cleanliness")]
        public int? Cleanliness { get; set; }

        [JsonPropertyName("Check in / front desk")]
        public int? CheckInFrontDesk { get; set; }

        [JsonPropertyName("Overall")]
        public int? Overall { get; set; }

        [JsonPropertyName("Value")]
        public int? Value { get; set; }

        [JsonPropertyName("Rooms")]
        public int? Rooms { get; set; }

        [JsonPropertyName("Location")]
        public int? Location { get; set; }

        [JsonPropertyName("Sleep Quality")]
        public int? SleepQuality { get; set; }
    }

    public class Review
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("ratings")]
        public Ratings Ratings { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; }
    }

    [CouchbaseKeyspace("hotel")]
    public class Hotel
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("address")]
        public string Address { get; set; }

        [JsonPropertyName("directions")]
        public string Directions { get; set; }

        [JsonPropertyName("phone")]
        public string Phone { get; set; }

        [JsonPropertyName("tollfree")]
        public string Tollfree { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("fax")]
        public string Fax { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("checkin")]
        public string Checkin { get; set; }

        [JsonPropertyName("checkout")]
        public string Checkout { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; }

        [JsonPropertyName("geo")]
        public Geo Geo { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("city")]
        public string City { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("reviews")]
        public List<Review>? Reviews { get; set; }

        [JsonPropertyName("public_likes")]
        public List<string>? PublicLikes { get; set; }

        [JsonPropertyName("vacancy")]
        public bool? Vacancy { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("alias")]
        public string Alias { get; set; }

        [JsonPropertyName("pets_ok")]
        public bool? PetsOk { get; set; }

        [JsonPropertyName("free_breakfast")]
        public bool? FreeBreakfast { get; set; }

        [JsonPropertyName("free_internet")]
        public bool? FreeInternet { get; set; }

        [JsonPropertyName("free_parking")]
        public bool? FreeParking { get; set; }
    }
}