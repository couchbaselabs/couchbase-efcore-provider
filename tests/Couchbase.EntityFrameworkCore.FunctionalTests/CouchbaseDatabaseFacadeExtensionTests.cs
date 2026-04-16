
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;


[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseDatabaseFacadeExtensionTests(
    TravelSampleFixture travelSampleFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task Test_GetCouchbaseClientAsync()
    {
        var travelSampleContext = travelSampleFixture.DbContext;
        var cluster = await travelSampleContext.Database.GetCouchbaseClientAsync().ConfigureAwait(false);
        Assert.NotNull(cluster);
    }
}
