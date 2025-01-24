using System.Net;
using ContosoUniversity.Data;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

[Collection(CouchbaseTestingCollection.Name)]
public class DependencyInjectionTests
{
    private readonly CouchbaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DependencyInjectionTests(CouchbaseFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _output = testOutputHelper;
    }
    

    [Fact]
    public async Task Check_That_More_Than_One_Context_Can_Be_Injected()
    {
        var bloggingContext = _fixture.BloggingContext;
        var travelSampleContext = _fixture.TravelSampleContext;

        var blogs = bloggingContext.Blogs.Take(1);
        var airlines = travelSampleContext.Airlines.Take(1);
        
        var blog = await blogs.FirstAsync();
        
        Assert.Single(blogs);
        Assert.Single(airlines);
    }

    private static ILoggerFactory _loggerFactory = null!;
    [Fact]
    public async Task Test()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCouchbase<BloggingContext>(
            new ClusterOptions()
                .WithConnectionString("couchbase://localhost")
                .WithCredentials("Administrator", "password")
                .WithLogging(_loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "Content";
                couchbaseDbContextOptions.Scope = "Blogs";
            });

        services.AddCouchbase<CouchbaseFixture.TravelSampleDbContext>(new ClusterOptions()
                .WithConnectionString("couchbase://localhost")
                .WithCredentials("Administrator", "password")
                .WithLogging(_loggerFactory),
            configuration =>
            {
                configuration.Bucket = "travel-sample";
                configuration.Scope = "inventory";
            });
        
        services.AddKeyedCouchbase("mine", options =>
        {
            options.ConnectionString = "couchbase://localhost";
            options.WithCredentials("Administrator", "password");
            options.WithLogging(_loggerFactory);
        });

        var provider = services.BuildServiceProvider();
        var boo = provider.GetRequiredKeyedService<IClusterProvider>("mine");
        var bloggingContext = provider.GetRequiredService<BloggingContext>();
        var travelSampleContext = provider.GetRequiredService<CouchbaseFixture.TravelSampleDbContext>();

        var cluster = await boo.GetClusterAsync();
        var result = cluster.QueryAsync<dynamic>("SELECT 1;");
        Assert.NotNull(result);
        
        bloggingContext.Update(new Blog{BlogId = 1, Url = "http://localhost/blogs"});
        await bloggingContext.SaveChangesAsync();
        
        var blogs = bloggingContext.Blogs.Take(1);
        var airlines = travelSampleContext.Airlines.Take(1);

        var airline = await airlines.FirstAsync();
        var blog = await blogs.FirstAsync();
        Assert.Single(blogs);
        Assert.Single(airlines);
    }
}