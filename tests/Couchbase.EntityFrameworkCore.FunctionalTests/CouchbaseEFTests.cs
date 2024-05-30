using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

public class CouchbaseEFTests
{
    private readonly ITestOutputHelper _outputHelper;

    public CouchbaseEFTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }
    
    [Fact]
    public async Task Test()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });
        
        var options = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory);

        var context = new TravelSampleDbContext(options);
        var airline = new Airline
        {
            Type = "airline",
            Id = 10,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };
            
        context.Add(airline);
        airline.Name = "foo";
        context.Update(airline);
        
        var found = context.Find<Airline>("airline", 10);
        context.Remove(airline);
        await context.SaveChangesAsync();
    }

    public class TravelSampleDbContext : DbContext
    {
        private readonly ClusterOptions _clusterOptions;

        public TravelSampleDbContext(ClusterOptions clusterOptions)
        {
            _clusterOptions = clusterOptions;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseCouchbase(_clusterOptions);
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Airline>().HasKey(x=>new {x.Type, x.Id});//composite key mapping
        }
    }
    
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
        public int Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }
 
}