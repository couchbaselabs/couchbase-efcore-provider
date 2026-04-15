using ContosoUniversity.Data;
using Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

[JetBrains.Annotations.UsedImplicitly]
public class TravelSampleFixture : CouchbaseFixtureBase
{
    protected override string ScopeName => "inventory";
    
    protected override string DatabaseName => "travel-sample";

    protected override Task LoadDataAsync()
    {
        return Task.CompletedTask;
    }
    
    public TravelSampleDbContext CreateDbContext()
    {
        return new TravelSampleDbContext();
    }

    public override Task InitializeAsync()
    {
        DbContext = new TravelSampleDbContext();
        return base.InitializeAsync();
    }
}