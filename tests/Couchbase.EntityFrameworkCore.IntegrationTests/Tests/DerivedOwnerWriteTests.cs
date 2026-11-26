using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// A derived entity (TPH) with an owned type declared on its base type used to be written twice in
/// one save: an insert and an upsert sent at the same time, so an add failed with
/// <c>DocumentExistsException</c> whenever the upsert landed first. The unit tests in
/// <c>CouchbaseOwnerWriteTests</c> check the single write deterministically; this checks the round
/// trip on a cluster, reading the documents directly.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class DerivedOwnerWriteTests(BloggingFixture fixture)
{
    [Fact]
    public async Task DerivedEntity_WithOwnedTypeOnTheBaseType_IsSavedAndUpdated()
    {
        var collectionName = "derivedowner" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var ctx = CreateContext(collectionName))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Animals.AddRange(
                    new Animal { Id = 1, Name = "Cat", Tag = new Tag { Text = "a" } },
                    new Dog { Id = 2, Name = "Rex", Tag = new Tag { Text = "b" }, Collar = new Collar { Color = "c" } });
                await ctx.SaveChangesAsync();

                // Change the derived entity and its owned type declared on the base type.
                var dog = (Dog)ctx.Animals.Local.Single(a => a.Id == 2);
                dog.Name = "Max";
                dog.Tag.Text = "b2";
                await ctx.SaveChangesAsync();
            }

            // Read the documents directly: this test is about what the save writes.
            var cluster = await global::Couchbase.Cluster.ConnectAsync(fixture.Host, fixture.Username, fixture.Password);
            try
            {
                var collection = (await cluster.BucketAsync(fixture.BucketName))
                    .Scope(fixture.ScopeName).Collection(collectionName);
                var dogDocument = (await collection.GetAsync("2")).ContentAs<Dictionary<string, object?>>()!;
                Assert.Equal("Max", dogDocument["Name"]?.ToString());
                Assert.Equal("b2", dogDocument["Text"]?.ToString());
                Assert.Equal("c", dogDocument["Color"]?.ToString());
                Assert.Equal("a", (await collection.GetAsync("1")).ContentAs<Dictionary<string, object?>>()!["Text"]?.ToString());
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private AnimalContext CreateContext(string collectionName)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnimalContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.AutoCreateIndexes = true;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return new AnimalContext(optionsBuilder.Options, collectionName);
    }

    private async Task DropCollectionAsync(string collectionName)
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class Animal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Tag Tag { get; set; } = null!;
    }

    public class Dog : Animal
    {
        public Collar Collar { get; set; } = null!;
    }

    // Its own type and property name, and the test reads the documents directly, so it does not
    // depend on how owned types map to document fields (CBEF-136).
    public class Collar
    {
        public string Color { get; set; } = string.Empty;
    }

    public class Tag
    {
        public string Text { get; set; } = string.Empty;
    }

    public class AnimalContext(DbContextOptions options, string collectionName) : DbContext(options)
    {
        public DbSet<Animal> Animals { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.Property(a => a.Id).ValueGeneratedNever();
                b.OwnsOne(a => a.Tag);
            });
            modelBuilder.Entity<Dog>(b =>
            {
                b.HasBaseType<Animal>();
                b.OwnsOne(d => d.Collar);
            });
        }
    }
}
