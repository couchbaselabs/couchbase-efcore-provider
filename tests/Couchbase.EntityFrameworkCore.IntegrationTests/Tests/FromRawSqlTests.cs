using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class FromRawSqlTests(
    BloggingFixture bloggingFixture,
    TravelSampleFixture travelSampleFixture,
    ITestOutputHelper outputHelper)
{
    private readonly ITestOutputHelper _outputHelper = outputHelper;

    [Fact]
    public async Task Test_META()
    {
        await using var context = bloggingFixture.GetDbContext();
        var statement = "SELECT `b`.* FROM `default`.`blogs`.`blog` as `b` WHERE META().id = \"2\"";
        var blog = await context.Blogs.FromSqlRaw(statement).AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(blog);
    }
    
    [Fact]
    public async Task Test_META2()
    {
        var pageSize = 10;
        var skip = 0;
        var airportCode = "sfo";

        await using var context = travelSampleFixture.GetDbContext();
        const string sql = @"SELECT DISTINCT route.destinationairport
            FROM `travel-sample`.`inventory`.`airport` AS airport
            JOIN `travel-sample`.`inventory`.`route` AS route
              ON route.sourceairport = airport.faa
            WHERE LOWER(airport.faa) = {0}
              AND route.stops = 0
            ORDER BY route.destinationairport
            LIMIT {1}
            OFFSET {2}";

        var destinations = await context.Set<TravelSampleFixture.DestinationAirport>()
            .FromSqlRaw(sql, airportCode, pageSize, skip).ToListAsync();
        
        Assert.NotNull(destinations);
    }
    
    [Fact]
    public async Task Test_FromSqlRaw_With_Parameters()
    {
        await using var context = bloggingFixture.GetDbContext();
        var query = "SELECT p.* FROM `default`.`blogs`.`person` as p WHERE personId={0}";
        var person = await context.Set<BloggingFixture.Person>()
            .FromSqlRaw(query, 1)
            .FirstOrDefaultAsync();

        Assert.NotNull(person);
    }

    [Fact]
    public async Task Test_FromRaw_Throws_NotImplementedException()
    {
        await using var context = bloggingFixture.GetDbContext();

        // CouchbaseFromSqlQueryingEnumerable<T>.GetEnumerator() (sync) is unimplemented for
        // non-composed FromSql-style queries; see Test_FromSqlRaw_ToList_Throws_NotImplementedException
        // below for the equivalent FromSqlRaw case.
        Assert.Throws<NotImplementedException>(()=>context.Blogs
            .FromSql($"SELECT * FROM `default`.`blogs`.`blog`")
            .ToList());
    }

    [Fact]
    public async Task Test_FromSql_ToListAsync_Returns_Results()
    {
        // Only the synchronous enumeration path (see Test_FromRaw_Throws_NotImplementedException
        // and Test_FromSqlRaw_ToList_Throws_NotImplementedException) is unimplemented for
        // non-composed FromSql-style queries — the async path is a separate, fully-implemented
        // code path (CouchbaseFromSqlQueryingEnumerable.GetAsyncEnumerator).
        await using var context = bloggingFixture.GetDbContext();

        var results = await context.Blogs
            .FromSql($"SELECT `b`.* FROM `default`.`blogs`.`blog` AS `b` WHERE META(`b`).id = \"1\"")
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(1, results.Count);
    }

    [Fact]
    public async Task Test_FromSqlRaw_ToList_Throws_NotImplementedException()
    {
        // The sync-enumeration gap isn't specific to interpolated FromSql (see
        // Test_FromRaw_Throws_NotImplementedException above) — CouchbaseFromSqlQueryingEnumerable
        // is used for any non-composed FromSql-style query, so FromSqlRaw hits the same
        // unimplemented GetEnumerator() when enumerated synchronously.
        await using var context = bloggingFixture.GetDbContext();

        Assert.Throws<NotImplementedException>(() => context.Blogs
            .FromSqlRaw("SELECT * FROM `default`.`blogs`.`blog`")
            .ToList());
    }
    
    [Fact]
    public async Task Test_FromSqlRaw_Returns_Results()
    {
        await using var context = bloggingFixture.GetDbContext();
        
        // Query using META().id which is the document key - this matches the working Test_META pattern
        // BlogId 1 is seeded data that should always exist
        var results = await context.Blogs
            .FromSqlRaw(
                "SELECT `b`.* FROM `default`.`blogs`.`blog` AS `b` WHERE META(`b`).id = \"1\"")
            .AsNoTracking()
            .ToListAsync();
        
        Assert.Equal(1, results.Count);
    }
}