using ContosoUniversity.Data;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
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
        logger.LogError(ex, "An error occurred creating or seeding the database.");
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
        var statement =
            $"CREATE PRIMARY INDEX IF NOT EXISTS ON `{bucketName}`.`{scopeName}`.`{collection}`";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await cluster.QueryAsync<dynamic>(statement);
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
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        return new CouchbaseConnectionInfo(
            ConnectionString: $"{uri.Scheme}://{uri.Host}:{uri.Port}",
            Username: userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "Administrator",
            Password: userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            BucketName: uri.AbsolutePath.TrimStart('/'));
    }
}
