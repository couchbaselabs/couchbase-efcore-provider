using System.Text.Json.Serialization;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

[Collection(CouchbaseTestingCollection.Name)]
public class FromRawSqlTests
{
    private readonly CouchbaseFixture _couchbaseFixture;
    private readonly ITestOutputHelper _outputHelper;

    public FromRawSqlTests(CouchbaseFixture couchbaseFixture, ITestOutputHelper outputHelper)
    {
        _couchbaseFixture = couchbaseFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_META()
    {
        using (var context = new BloggingContext())
        {
            await _couchbaseFixture.InitializeBloggingAsync();
            var statement = "SELECT `b`.* FROM `Content`.`Blogs`.`Blog` as `b` WHERE META().id = \"2\"";
            var blog = await context.Blogs.FromSqlRaw(statement).AsNoTracking().FirstOrDefaultAsync();
            
            Assert.NotNull(blog);
        }
    }
    
    [Fact]
    public async Task Test_META2()
    {
        var pageSize = 10;
        var skip = 0;
        var airportCode = "sfo";

        using (var context = new CouchbaseFixture.TravelSampleDbContext())
        {
            const string sql = @"SELECT DISTINCT route.destinationairport
                FROM `travel-sample`.`inventory`.`airport` AS airport
                JOIN `travel-sample`.`inventory`.`route` AS route
                  ON route.sourceairport = airport.faa
                WHERE LOWER(airport.faa) = {0}
                  AND route.stops = 0
                ORDER BY route.destinationairport
                LIMIT {1}
                OFFSET {2}";

            var destinations = await context.Set<DestinationAirport>()
                .FromSqlRaw(sql, airportCode, pageSize, skip).ToListAsync();
            
            Assert.NotNull(destinations);
        }
    }
    
    public class DestinationAirport
    {
        //public int Id { get; set; }
        
        [JsonPropertyName("destinationairport")]
        public string Destinationairport { get; set; } = string.Empty;
    }
}