using ContosoUniversity.Data;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Serilog.Extensions.Logging.File;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllersWithViews();

// The Couchbase connection is provisioned by the Contoso.AppHost (Aspire) and injected as the
// "Universities" connection string. Format: couchbase://user:pass@host:port/bucket
var connectionString = builder.Configuration.GetConnectionString("Universities")
    ?? throw new InvalidOperationException(
        "Connection string 'Universities' was not found. Run the app through the Contoso.AppHost.");

var couchbase = CouchbaseConnectionInfo.Parse(connectionString);
const string scopeName = "Contoso";

builder.Services.AddDbContext<SchoolContext>(options =>
{
    var clusterOptions = new ClusterOptions()
        .WithPasswordAuthentication(couchbase.Username, couchbase.Password)
        .WithConnectionString(couchbase.ConnectionString)
        .WithLogging(
            LoggerFactory.Create(
                logging =>
                {
                    logging.AddFilter(level => level >= LogLevel.Debug);
                    logging.AddFile("Logs/myapp-{Date}-blog-sdk.txt", LogLevel.Debug);
                }));
    options
        .UseCouchbase(clusterOptions,
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = couchbase.BucketName;
                couchbaseDbContextOptions.Scope = scopeName;
                // Read-after-write consistency: the app seeds at startup then immediately queries,
                // and controllers query right after writes. RequestPlus makes N1QL queries wait for
                // the index to reflect prior mutations (the SDK default, NotBounded, can read stale).
                couchbaseDbContextOptions.ScanConsistency = QueryScanConsistency.RequestPlus;
            });
    options.UseCamelCaseNamingConvention();
});

builder.Logging.AddFile("Logs/myapp-{Date}.txt");

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await InitializeDatabaseAsync(app, couchbase.BucketName, scopeName);

app.Run();

// Creates the query indexes the LINQ queries depend on, then seeds the example data.
// The AppHost has already created the bucket, scope, and collections and waited for them to be
// healthy; indexes are created here because the Aspire hosting DSL has no index primitive.
static async Task InitializeDatabaseAsync(IHost host, string bucketName, string scopeName)
{
    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<SchoolContext>();
        await CreatePrimaryIndexesAsync(context, bucketName, scopeName, logger);
        await DbInitializer.Initialize(context);
    }
    catch (Exception ex)
    {
        // Fail fast: a half-provisioned database (missing indexes or unseeded data) would only
        // surface as confusing runtime errors on later requests. Abort startup so a broken
        // Couchbase/Aspire run is immediately visible.
        logger.LogError(ex, "Database initialization failed; aborting startup.");
        throw;
    }
}

// Every entity collection needs a primary index so the app's N1QL (LINQ) queries can run.
// CREATE PRIMARY INDEX IF NOT EXISTS is idempotent, so this is safe on every startup.
static async Task CreatePrimaryIndexesAsync(SchoolContext context, string bucketName, string scopeName, ILogger logger)
{
    string[] collections =
    [
        "course", "enrollment", "person", "department", "officeAssignment", "courseAssignment"
    ];

    var cluster = await context.Database.GetCouchbaseClientAsync();

    // The query service can lag slightly behind bucket health on a cold container start; retry.
    const int maxAttempts = 10;
    foreach (var collection in collections)
    {
        // Build the keyspace via CouchbaseKeyspace so each identifier is backtick-escaped
        // (embedded backticks doubled) — bucket/scope come from the Aspire connection string.
        var keyspace = new CouchbaseKeyspace(bucketName, scopeName, collection).ToSqlString();
        var statement = $"CREATE PRIMARY INDEX IF NOT EXISTS ON {keyspace}";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Dispose the result (and drain its rows) so the query's resources/sockets are
                // released — awaiting QueryAsync alone does not dispose the IQueryResult.
                using var result = await cluster.QueryAsync<dynamic>(statement);
                await foreach (var _ in result.Rows) { }
                logger.LogInformation("Ensured primary index on {Bucket}.{Scope}.{Collection}",
                    bucketName, scopeName, collection);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex,
                    "Primary index creation for {Collection} failed (attempt {Attempt}/{Max}); retrying...",
                    collection, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}

// Parses an Aspire Couchbase connection string (couchbase://user:pass@host:port/bucket) into the
// pieces the SDK ClusterOptions and the EF provider need.
internal readonly record struct CouchbaseConnectionInfo(
    string ConnectionString, string Username, string Password, string BucketName)
{
    public static CouchbaseConnectionInfo Parse(string connectionString)
    {
        const string expectedFormat = "Expected format: couchbase://user:pass@host:port/bucket";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The Couchbase connection string is empty. " + expectedFormat);
        }

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"The Couchbase connection string '{connectionString}' is not a valid URI. " + expectedFormat);
        }

        // Omit the port when the URI doesn't specify one (Uri.Port is -1); appending ":-1"
        // would produce an invalid Couchbase connection string.
        var host = uri.IsDefaultPort || uri.Port < 0
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        // Credentials are required and must be complete. Fail fast with a clear message rather
        // than defaulting, which would surface later as a less actionable auth error.
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "The Couchbase connection string is missing a username and/or password. " + expectedFormat);
        }

        // A bucket segment (the URI path) is required.
        var bucketName = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(bucketName))
        {
            throw new InvalidOperationException(
                "The Couchbase connection string is missing a bucket segment. " + expectedFormat);
        }

        return new CouchbaseConnectionInfo(host, username, password, bucketName);
    }
}
