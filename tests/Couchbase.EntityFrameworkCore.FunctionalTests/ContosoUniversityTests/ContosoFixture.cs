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
    public ContosoContext DbContext { get; private set; }
    
    public Task InitializeAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });

        var clusterOptions = new ClusterOptions()
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory)
            .WithBuckets("contoso");

        var contextOptions = new DbContextOptions<SchoolContext>();
        DbContext = new ContosoContext(contextOptions, clusterOptions);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
       return Task.CompletedTask;
    }
}