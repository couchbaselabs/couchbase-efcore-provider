using System.Text.Json.Serialization;
using Couchbase.Core.Diagnostics.Metrics.AppTelemetry;
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
    private readonly BloggingFixture _bloggingFixture;
    private readonly ITestOutputHelper _outputHelper;

    public FromRawSqlTests(BloggingFixture bloggingFixture, TravelSampleFixture travelSampleFixture, ITestOutputHelper outputHelper)
    {
        _bloggingFixture = bloggingFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_META()
    {
        using (var context = new BloggingContext())
        {
            var statement = "SELECT `b`.* FROM `default`.`blogs`.`blog` as `b` WHERE META().id = \"2\"";
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

        using (var context = new TravelSampleDbContext())
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
    
    [Fact]
    public async Task Test_FromSqlRaw_With_Parameters()
    {
        await using var context = new BloggingContext();
        var query = "SELECT p.* FROM `default`.`blogs`.`person` as p WHERE personId={0}";
        var person = await context.Set<Person>()
            .FromSqlRaw(query, 1)
            .FirstOrDefaultAsync();

        Assert.NotNull(person);
    }

    [Fact]
    public async Task Test_FromRaw_Throws_NotImplementedException()
    {
        await using var context = new BloggingContext();

        //Exception is because of an incomplete implementation of CouchbaseDbDataReader
        Assert.Throws<NotImplementedException>(()=>context.Blogs
            .FromSql($"SELECT * FROM `default`.`blogs`.`blog`")
            .ToList());
    }
    
    [Fact]
    public async Task Test_FromSqlRaw_Returns_Results()
    {
        await using var context = new BloggingContext();
        var rating = 4;

        var results = await context.Blogs
            .FromSqlRaw(
                $"SELECT VALUE p FROM default.blogs.post p WHERE p.rating == {rating}")
            .ToListAsync();
        
        Assert.Equal(1, results.Count);
    }
    
    public class DestinationAirport
    {
        //public int Id { get; set; }
        
        [JsonPropertyName("destinationairport")]
        public string Destinationairport { get; set; } = string.Empty;
    }
}