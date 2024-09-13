using System.Text.Json.Serialization;
using Couchbase.EntityFrameworkCore.Metadata;
using Newtonsoft.Json;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Models;

[CouchbaseKeyspace("inventory", "airline")]
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