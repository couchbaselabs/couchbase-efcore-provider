using ContosoUniversity.Data;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;

public class ContosoFixture : CouchbaseFixtureBase
{
    public ILogger logger { get; private set; }

    protected override string ScopeName => "contoso";

    public ContosoContext CreateDbContext()
    {
        var contextOptions = new DbContextOptions<SchoolContext>();
        return new ContosoContext(contextOptions, Options);
    }

    protected override Task LoadDataAsync()
    {
        //nothing for now
        return Task.CompletedTask;
    }

    public override async Task InitializeAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });
        logger = loggerFactory.CreateLogger<ContosoFixture>();

        Options = new ClusterOptions()
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory);

        DbContext = CreateDbContext();
        await base.InitializeAsync();
    }
}