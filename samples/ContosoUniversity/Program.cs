using ContosoUniversity.Data;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Serilog.Extensions.Logging.File;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<SchoolContext>(options=>
    {
        var clusterOptions = new ClusterOptions()
            .WithCredentials("USERNAME", "PASSWORD")
            .WithConnectionString("couchbases://XXXX.cloud.couchbase.com")
            .WithLogging(
                LoggerFactory.Create(
                    builder =>
                        {
                            builder.AddFilter(level => level >= LogLevel.Debug);
                            builder.AddFile("Logs/myapp-{Date}-blog-sdk.txt", LogLevel.Debug);
                        }));
        options
            .UseCouchbase(clusterOptions,
                couchbaseDbContextOptions =>
                    {
                        couchbaseDbContextOptions.Bucket = "Universities";
                        couchbaseDbContextOptions.Scope = "Contoso";
                    });
        options.UseCamelCaseNamingConvention();
    });


builder.Logging.AddFile("Logs/myapp-{Date}.txt");

var app = builder.Build();

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
await CreateDbIfNotExists(app);

app.Run();

async Task CreateDbIfNotExists(IHost host)
{
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SchoolContext>();
           await DbInitializer.Initialize(context);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred creating the DB.");
        }
    }
}