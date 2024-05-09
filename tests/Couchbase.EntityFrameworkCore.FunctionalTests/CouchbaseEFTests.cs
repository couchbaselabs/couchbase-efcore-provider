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

        var cluster = await Cluster.ConnectAsync(options);
        var context = TravelSampleDbContext.Create(cluster);
        var airline = context.Find<Airline>("airline_", 10);//composite key
    }

    public class TravelSampleDbContext : DbContext
    {
        public TravelSampleDbContext(DbContextOptions options)
            : base(options)
        {
        }

        public static TravelSampleDbContext Create(ICluster cluster) =>
            new(new DbContextOptionsBuilder<TravelSampleDbContext>()
                .UseCouchbase(cluster)
                .Options);

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