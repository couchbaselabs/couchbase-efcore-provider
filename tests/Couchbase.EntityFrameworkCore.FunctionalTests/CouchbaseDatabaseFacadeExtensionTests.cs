
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;


[Collection(CouchbaseTestingCollection.Name)]
public class CouchbaseDatabaseFacadeExtensionTests
{
    private readonly CouchbaseFixture _couchbaseFixture;
    private readonly ITestOutputHelper _outputHelper;

    public CouchbaseDatabaseFacadeExtensionTests(CouchbaseFixture couchbaseFixture, ITestOutputHelper outputHelper)
    {
        _couchbaseFixture = couchbaseFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_GetCouchbaseClientAsync()
    {
        var travelSampleContext = _couchbaseFixture.TravelSampleContext;
        var cluster = await travelSampleContext.Database.GetCouchbaseClientAsync().ConfigureAwait(false);
        Assert.NotNull(cluster);
    }
}
