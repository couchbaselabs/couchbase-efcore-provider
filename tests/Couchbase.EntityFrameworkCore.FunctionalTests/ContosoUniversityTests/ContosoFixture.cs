using ContosoUniversity.Data;
using Couchbase.Core.IO.Serializers;
using Couchbase.Core.IO.Transcoders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;

public class ContosoFixture : IAsyncLifetime
{
    public ClusterOptions ClusterOptions { get; private set; }
    public ILogger logger { get; private set; }

    public ContosoContext DbContext()
    {
        var contextOptions = new DbContextOptions<SchoolContext>();
        return new ContosoContext(contextOptions, ClusterOptions);
    }
    
    public Task InitializeAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });
        logger = loggerFactory.CreateLogger<ContosoFixture>();

        ClusterOptions = new ClusterOptions()
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory);
        
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
       return Task.CompletedTask;
    }
}