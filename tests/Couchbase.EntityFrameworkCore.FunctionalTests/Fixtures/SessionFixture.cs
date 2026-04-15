namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public class SessionFixture : CouchbaseFixtureBase
{
    protected override string ScopeName => "sessions";
    
    protected override string DatabaseName => "default";

    protected override Task LoadDataAsync()
    {
        return Task.CompletedTask;
    }
    
    public SessionContext CreateDbContext()
    {
        return new SessionContext(Options);
    }

    public override Task InitializeAsync()
    {
        Options = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithCredentials("Administrator", "password");

        DbContext = CreateDbContext();
        return base.InitializeAsync();
    }
}