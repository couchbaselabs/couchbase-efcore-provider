using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCode.IntegrationTests.Models;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class DependencyInjectionTests(
    BloggingFixture bloggingFixture,
    TravelSampleFixture travelSampleFixture,
    ITestOutputHelper testOutputHelper)
{
    private readonly ITestOutputHelper _output = testOutputHelper;

    private static ILoggerFactory _loggerFactory = null!;
    [Fact]
    public async Task Check_That_More_Than_One_Context_Can_Be_Injected()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
        });
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCouchbase<BloggingDbContext>(
            new ClusterOptions()
                .WithConnectionString(bloggingFixture.Host)
                .WithCredentials(bloggingFixture.Username, bloggingFixture.Password)
                .WithLogging(_loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "default";
                couchbaseDbContextOptions.Scope = "blogs";
            },
            options => options.UseCamelCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        services.AddCouchbase<TravelSampleDbContext>(new ClusterOptions()
                .WithConnectionString(travelSampleFixture.Host)
                .WithCredentials(travelSampleFixture.Username, travelSampleFixture.Password)
                .WithLogging(_loggerFactory),
            configuration =>
            {
                configuration.Bucket = "travel-sample";
                configuration.Scope = "inventory";
            },
            options => options.UseCamelCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        services.AddKeyedCouchbase("mine", options =>
        {
            options.ConnectionString = bloggingFixture.Host;
            options.WithCredentials(bloggingFixture.Username, bloggingFixture.Password);
            options.WithLogging(_loggerFactory);
        });

        var provider = services.BuildServiceProvider();
        var boo = provider.GetRequiredKeyedService<IClusterProvider>("mine");
        var bloggingContext = provider.GetRequiredService<BloggingDbContext>();
        var travelSampleContext = provider.GetRequiredService<TravelSampleDbContext>();

        var cluster = await boo.GetClusterAsync();
        var result = cluster.QueryAsync<dynamic>("SELECT 1;");
        Assert.NotNull(result);

        bloggingContext.Update(new BloggingFixture.Blog{BlogId = 1, Url = "http://localhost/blogs"});
        await bloggingContext.SaveChangesAsync();

        var blogs = await bloggingContext.Blogs.Take(1).ToListAsync();
        var airlines = await travelSampleContext.Airlines.Take(1).ToListAsync();

        Assert.Single(blogs);
        Assert.Single(airlines);
    }
}