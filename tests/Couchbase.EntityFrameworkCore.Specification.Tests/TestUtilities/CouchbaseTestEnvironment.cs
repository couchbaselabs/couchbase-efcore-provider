using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// Connection settings for the Couchbase cluster the specification tests run against.
/// </summary>
/// <remarks>
/// If <c>COUCHBASE_CONNECTION_STRING</c> is set, the suite uses that cluster (with
/// <c>COUCHBASE_USERNAME</c>, <c>COUCHBASE_PASSWORD</c> and <c>COUCHBASE_BUCKET</c>) — useful for CI
/// or a hosted cluster. Otherwise it starts the repo's Aspire <c>AppHost</c> (the same one the
/// integration tests use) once per test run and reads the generated connection string from it.
/// </remarks>
public static class CouchbaseTestEnvironment
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(180);

    private static readonly Lazy<Settings> Resolved = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    // Kept alive for the whole test run so the container isn't torn down mid-suite; Aspire stops
    // the resources when the test process exits.
    private static DistributedApplication? _app;

    public static string ConnectionString => Resolved.Value.ConnectionString;

    public static string Username => Resolved.Value.Username;

    public static string Password => Resolved.Value.Password;

    /// <summary>The bucket every spec-test scope lives in. It must already exist.</summary>
    public static string Bucket => Resolved.Value.Bucket;

    private sealed record Settings(string ConnectionString, string Username, string Password, string Bucket);

    private static Settings Resolve()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("COUCHBASE_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(explicitConnectionString))
        {
            return new Settings(
                explicitConnectionString,
                Environment.GetEnvironmentVariable("COUCHBASE_USERNAME") ?? "Administrator",
                Environment.GetEnvironmentVariable("COUCHBASE_PASSWORD") ?? "password",
                Environment.GetEnvironmentVariable("COUCHBASE_BUCKET") ?? "default");
        }

        return StartAppHostAsync().GetAwaiter().GetResult();
    }

    private static async Task<Settings> StartAppHostAsync()
    {
        const string resourceName = "default";
        var cancellationToken = CancellationToken.None;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(cancellationToken);
        _app = await builder.BuildAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await _app.StartAsync(cancellationToken).WaitAsync(StartupTimeout, cancellationToken);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken)
            .WaitAsync(StartupTimeout, cancellationToken);

        // Aspire connection string format: couchbase://user:pass@host:port/bucketname
        var connectionString = await _app.GetConnectionStringAsync(resourceName, cancellationToken)
            ?? throw new InvalidOperationException($"AppHost resource '{resourceName}' has no connection string.");
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':');

        return new Settings(
            $"couchbase://{uri.Host}:{uri.Port}",
            userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "Administrator",
            userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            uri.AbsolutePath.TrimStart('/'));
    }
}
