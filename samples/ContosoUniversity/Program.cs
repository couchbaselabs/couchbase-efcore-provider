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

await InitializeDatabaseAsync(app);

app.Run();

// Creates the query indexes the LINQ queries depend on, then seeds the example data.
// The AppHost has already created the bucket, scope, and collections and waited for them to be
// healthy; indexes are created here because the Aspire hosting DSL has no index primitive.
static async Task InitializeDatabaseAsync(IHost host)
{
    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<SchoolContext>();
        await CreatePrimaryIndexesAsync(context, logger);
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
static async Task CreatePrimaryIndexesAsync(SchoolContext context, ILogger logger)
{
    // Derive the distinct keyspaces from the EF model rather than hardcoding a list, so a new
    // entity/collection is picked up automatically instead of being silently skipped. The table
    // name is the full Bucket.Scope.Collection keyspace; owned types are embedded in their owner's
    // document and have no keyspace of their own.
    var keyspaces = context.Model.GetEntityTypes()
        .Where(e => !e.IsOwned())
        .Select(e => e.GetTableName())
        .Where(t => !string.IsNullOrEmpty(t))
        .Distinct()
        .Select(t => CouchbaseKeyspace.Parse(t!))
        .ToList();

    var cluster = await context.Database.GetCouchbaseClientAsync();

    // The query service can lag slightly behind bucket health on a cold container start; retry.
    const int maxAttempts = 10;
    foreach (var keyspace in keyspaces)
    {
        // ToSqlString backtick-escapes each identifier (embedded backticks doubled).
        var sqlKeyspace = keyspace.ToSqlString();
        var statement = $"CREATE PRIMARY INDEX IF NOT EXISTS ON {sqlKeyspace}";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Dispose the result (and drain its rows) so the query's resources/sockets are
                // released — awaiting QueryAsync alone does not dispose the IQueryResult.
                using var result = await cluster.QueryAsync<dynamic>(statement);
                await foreach (var _ in result.Rows) { }
                logger.LogInformation("Ensured primary index on {Keyspace}", sqlKeyspace);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex,
                    "Primary index creation for {Keyspace} failed (attempt {Attempt}/{Max}); retrying...",
                    sqlKeyspace, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    // CREATE PRIMARY INDEX can return before the index is online/queryable, so on a cold start the
    // seeding queries (e.g. Students.AnyAsync) could run against a not-yet-usable index and fail.
    // Wait until every primary index reports state='online' before continuing.
    var onlineDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
    foreach (var keyspace in keyspaces)
    {
        Exception? lastError = null;
        while (true)
        {
            var online = false;
            try
            {
                using var result = await cluster.QueryAsync<int>(
                    "SELECT RAW COUNT(*) FROM system:indexes WHERE is_primary = true AND state = 'online' "
                    + "AND bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection",
                    new global::Couchbase.Query.QueryOptions()
                        .Parameter("bucket", keyspace.Bucket)
                        .Parameter("scope", keyspace.Scope)
                        .Parameter("collection", keyspace.Collection));
                await foreach (var count in result.Rows) { online = count > 0; break; }
                lastError = null;
            }
            catch (Exception ex) when (DateTime.UtcNow <= onlineDeadline)
            {
                lastError = ex; // transient (query service busy right after DDL); keep polling
            }

            if (online)
            {
                logger.LogInformation("Primary index online for {Keyspace}", keyspace.ToSqlString());
                break;
            }
            if (DateTime.UtcNow > onlineDeadline)
            {
                throw new TimeoutException(
                    $"Primary index for {keyspace.ToSqlString()} did not come online within 60s.", lastError);
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
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
            // Don't include the raw value — it may contain embedded credentials (user:pass).
            throw new InvalidOperationException(
                "The Couchbase connection string is not a valid URI. " + expectedFormat);
        }

        // A non-Couchbase scheme (e.g. http://, a copy-pasted connection string for another
        // service) would otherwise pass validation here and fail later in a less actionable way.
        if (uri.Scheme != "couchbase" && uri.Scheme != "couchbases")
        {
            throw new InvalidOperationException(
                "The Couchbase connection string has an unsupported scheme "
                + $"'{uri.Scheme}'. " + expectedFormat);
        }

        // Rebuild from the URI's components rather than interpolating uri.Host/uri.Port directly:
        // GetComponents handles IPv6 bracket formatting correctly and preserves any query string
        // (e.g. ?network=external), which the SDK's connection string parser also supports.
        // SchemeAndServer excludes UserInfo and omits a port that wasn't specified.
        var host = uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Query, UriFormat.UriEscaped);

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
