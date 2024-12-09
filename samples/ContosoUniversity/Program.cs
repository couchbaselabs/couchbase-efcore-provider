using ContosoUniversity.Data;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Serilog.Extensions.Logging.File;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<SchoolContext>(options=> options
    .UseCouchbase<INamedBucketProvider>(new ClusterOptions()
        .WithCredentials("Ajax", "GE9jk9i28L2Psg@")
        .WithConnectionString("couchbases://cb.umolxgoqkdzpvdvo.cloud.couchbase.com"),
        couchbaseDbContextOptions =>
        {
            couchbaseDbContextOptions.Bucket = "Universities";
            couchbaseDbContextOptions.Scope = "Contoso";
        }));


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

CreateDbIfNotExists(app);

app.Run();

void CreateDbIfNotExists(IHost host)
{
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SchoolContext>();
            DbInitializer.Initialize(context);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred creating the DB.");
        }
    }
}