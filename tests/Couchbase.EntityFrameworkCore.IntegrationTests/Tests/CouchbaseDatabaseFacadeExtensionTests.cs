using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseDatabaseFacadeExtensionTests(
    TravelSampleFixture fixture, 
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task Test_GetCouchbaseClientAsync()
    {
        await using var context = fixture.GetDbContext();
        var cluster = await context.Database.GetCouchbaseClientAsync();
        Assert.NotNull(cluster);
    }
}
