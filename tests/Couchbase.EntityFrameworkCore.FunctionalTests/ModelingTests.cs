using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

[Collection(CouchbaseTestingCollection.Name)]
public class ModelingTests
{
    private readonly CouchbaseFixture _couchbaseFixture;
    private readonly ITestOutputHelper _outputHelper;

    public ModelingTests(CouchbaseFixture couchbaseFixture,
        ITestOutputHelper outputHelper)
    {
        _couchbaseFixture = couchbaseFixture;
        _outputHelper = outputHelper;
    }

    private class ModelingBucketOnlyContext : DbContext
    {
        public DbSet<Blog> Blogs { get; set; }
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.UseCouchbase(new ClusterOptions()
                    .WithCredentials("Administrator", "password")
                    .WithConnectionString("couchbase://localhost"),
                couchbaseDbContextOptions =>
                {
                    couchbaseDbContextOptions.Bucket = "Content";
                });
            optionsBuilder.UseCamelCaseNamingConvention();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blog>()
                .ToCouchbaseCollection(this,"Blogs","Blog");
            base.OnModelCreating(modelBuilder);
        }
    }

    [Fact]
    public async Task Test_Modeling_Bucket_Only()
    {
        using var context = new ModelingBucketOnlyContext();
        await _couchbaseFixture.InitializeBloggingAsync();
        var blog = await context.Blogs.FirstOrDefaultAsync();
        
        Assert.NotNull(blog);
    }
}