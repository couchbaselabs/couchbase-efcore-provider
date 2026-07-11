using Aspire.Hosting;
using Couchbase.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    // GetDbContext() calls CreateDbContextOptions on every invocation — often many times per
    // test, across hundreds of tests sharing this fixture instance via ICollectionFixture<>.
    // Building a fresh ILoggerFactory per call was suspected to force EF Core's internal
    // ServiceProviderCache to treat each call as a distinct configuration (the
    // ManyServiceProvidersCreatedWarning scenario) — confirmed NOT the case here (this
    // ILoggerFactory feeds the Couchbase SDK's own client logging via ClusterOptions.WithLogging,
    // which is unrelated to EF Core's own UseLoggerFactory/CoreOptionsExtension hash; verified via
    // reflection that CouchbaseOptionsExtension/CoreOptionsExtension/NamingConventionsOptionsExtension
    // all hash identically regardless of logger-factory identity). Still cached per fixture
    // instance as a harmless improvement — no reason to rebuild it on every call, since its
    // configuration (ScopeName, log file path) never changes after construction.
    private ILoggerFactory? _loggerFactory;

    protected DbContextOptions<TContext> CreateDbContextOptions<TContext>() where TContext : DbContext
    {
        _loggerFactory ??= LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/" + ScopeName + "-{Date}.txt", LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        // The full integration suite legitimately spans many distinct bucket/scope/DI-container
        // combinations across ~250 tests by design (multi-bucket and DI-isolation tests each
        // need their own internal service provider to prove real isolation) — this crosses EF
        // Core's own ">20 internal service providers" heuristic, which defaults to throwing.
        // Suppress it here: it's the officially documented way to acknowledge "yes, this many
        // providers is expected" (see the warning's own message), not a sign of a real leak.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(Host)
                .WithPasswordAuthentication(Username, Password)
                .WithLogging(_loggerFactory),
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
        _loggerFactory?.Dispose();
        await _app.DisposeAsync();
        await _appHost.DisposeAsync();
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
        _app.Dispose();
        _appHost.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _loggerFactory?.Dispose();
        await _app.DisposeAsync();
        await _appHost.DisposeAsync();
    }
}