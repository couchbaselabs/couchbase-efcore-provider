using Aspire.Hosting;
using Couchbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;

public abstract class CouchbaseFixture<T> : IDisposable, IAsyncDisposable, IAsyncLifetime where T : DbContext
{
    private readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(180);
    private IDistributedApplicationTestingBuilder _appHost;
    private DistributedApplication _app;
    
    public string Username { get; private set; }

    public string Password { get; private set; }

    public string Host { get; private set; }
    
    public virtual string ResourceName { get; set; } = "default";

    public virtual string BucketName { get; private set; }
    
    public abstract string ScopeName { get; }

    public abstract T GetDbContext();

    public abstract Task LoadDataAsync();

    protected DbContextOptions<TContext> CreateDbContextOptions<TContext>() where TContext : DbContext
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/" + ScopeName + "-{Date}.txt", LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(Host)
                .WithPasswordAuthentication(Username, Password)
                .WithLogging(loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = BucketName;
                couchbaseDbContextOptions.Scope = ScopeName;
                // Read-after-write consistency: tests seed via KV then immediately query through
                // the N1QL index, so wait for the index to reflect prior mutations. Avoids
                // intermittent stale reads under the default NotBounded scan consistency.
                couchbaseDbContextOptions.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        optionsBuilder.UseCamelCaseNamingConvention();

        return optionsBuilder.Options;
    }

    public virtual async Task InitializeAsync()
    {
        var cancellationToken = CancellationToken.None;
        _appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(cancellationToken);

        _app = await _appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await _app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(ResourceName, cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        var connectionString = await _app.GetConnectionStringAsync(ResourceName, cancellationToken);
        Assert.NotNull(connectionString);

        // Aspire Couchbase connection string format: couchbase://user:pass@host:port/bucketname
        var uri = new Uri(connectionString);
        Host = $"couchbase://{uri.Host}:{uri.Port}";
        var userInfo = uri.UserInfo.Split(':');
        Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "Administrator";
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        BucketName = uri.AbsolutePath.TrimStart('/');
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _app.DisposeAsync();
        await _appHost.DisposeAsync();
    }

    public void Dispose()
    {
        _app.Dispose();
        _appHost.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _appHost.DisposeAsync();
    }
}