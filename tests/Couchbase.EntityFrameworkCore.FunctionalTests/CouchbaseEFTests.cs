using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
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
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory);

        var context = new TravelSampleDbContext(options);
        var airline = new Airline
        {
            Type = "airline",
            Id = 11,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };
            
        //context.Add(airline);
        airline.Name = "foo";
        //context.Update(airline);
        
      //  var found = context.Find<Airline>("airline", 11);

      //var ab = await context.Airlines.FindAsync("airline", 11);
      var airlines1 = await context.Airlines
          .OrderBy(x => x.Id).ToListAsync<Airline>();

      foreach (var a in airlines1)
      {
          _outputHelper.WriteLine(a.ToString());
      }

      var airlines = await context.Airlines
          .OrderBy(x => x.Id)
          .FirstAsync();

      _outputHelper.WriteLine(airlines.ToString());
        
        //context.Remove(airline);
        await context.SaveChangesAsync();
    }

    [Fact]
    public void RunQuery()
    {
        var cluster = Cluster.ConnectAsync(new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithCredentials("Administrator", "password")).GetAwaiter().GetResult();

        var query = cluster.QueryAsync<Airline>("SELECT * from DEFAULT").GetAwaiter().GetResult();
        
    }
    
    
    [Fact]
    public async Task RunQueryAsync()
    {
        IServiceCollection serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder
            .AddFilter(level => level >= LogLevel.Debug)
        );
        var loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();
        loggerFactory.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);

        var options = new ClusterOptions()
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory);
        //options.HttpIgnoreRemoteCertificateMismatch = true;
        
        var cluster = await Cluster.ConnectAsync(options);

        var query = await cluster.QueryAsync<Airline>("SELECT t.* from `travel-sample` as t LIMIT 10");

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
            
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Airline>().HasKey(x=>new {x.Type, x.Id});
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

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}