using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Shadow properties are real data: a scalar shadow property and the shadow foreign key EF creates
/// for a relationship with no FK property must be written to the document and read back by a new
/// context. Before they were persisted, a document was stored as just its CLR properties and the
/// relationship was silently lost on reload.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class ShadowPropertyTests(BloggingFixture fixture) : IAsyncLifetime
{
    // EF caches the model (including collection names) per context type, so the names are static.
    private static readonly string AuthorCollection = "shadowauthors" + Guid.NewGuid().ToString("N");
    private static readonly string BookCollection = "shadowbooks" + Guid.NewGuid().ToString("N");

    private DbContextOptions<ShadowDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShadowDbContext>();
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
        _options = optionsBuilder.Options;

        await using var context = new ShadowDbContext(_options, AuthorCollection, BookCollection);
        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task ShadowScalarAndShadowForeignKey_SurviveAReloadInANewContext()
    {
        await using (var writer = new ShadowDbContext(_options, AuthorCollection, BookCollection))
        {
            var author = new ShadowAuthor { Id = 1, Name = "Ursula" };
            var book = new ShadowBook { Id = 10, Title = "Dispossessed", Author = author };
            writer.Add(author);
            writer.Add(book);
            writer.Entry(book).Property<int>("Rating").CurrentValue = 5;
            await writer.SaveChangesAsync();
        }

        await using (var reader = new ShadowDbContext(_options, AuthorCollection, BookCollection))
        {
            var book = await reader.Books.AsNoTracking()
                .Include(b => b.Author)
                .SingleAsync(b => b.Id == 10);

            Assert.Equal("Ursula", book.Author?.Name);

            var rating = await reader.Books
                .Where(b => b.Id == 10)
                .Select(b => EF.Property<int>(b, "Rating"))
                .SingleAsync();
            Assert.Equal(5, rating);

            var byForeignKey = await reader.Books
                .Where(b => EF.Property<int?>(b, "AuthorId") == 1)
                .Select(b => b.Title)
                .ToListAsync();
            Assert.Equal(["Dispossessed"], byForeignKey);
        }

        await using (var updater = new ShadowDbContext(_options, AuthorCollection, BookCollection))
        {
            var book = await updater.Books.SingleAsync(b => b.Id == 10);
            updater.Entry(book).Property<int>("Rating").CurrentValue = 4;
            await updater.SaveChangesAsync();
        }

        await using (var reader = new ShadowDbContext(_options, AuthorCollection, BookCollection))
        {
            var rating = await reader.Books
                .Where(b => b.Id == 10)
                .Select(b => EF.Property<int>(b, "Rating"))
                .SingleAsync();
            Assert.Equal(4, rating);
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            foreach (var name in new[] { AuthorCollection, BookCollection })
            {
                try
                {
                    await bucket.Collections.DropCollectionAsync(fixture.ScopeName, name);
                }
                catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
                {
                }
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public class ShadowAuthor
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<ShadowBook> Books { get; set; } = [];
    }

    public class ShadowBook
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;

        // No FK property: EF creates the shadow foreign key "AuthorId".
        public ShadowAuthor? Author { get; set; }
    }

    public class ShadowDbContext(DbContextOptions<ShadowDbContext> options, string authorCollection, string bookCollection)
        : DbContext(options)
    {
        public DbSet<ShadowAuthor> Authors { get; set; } = null!;
        public DbSet<ShadowBook> Books { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ShadowAuthor>(b =>
            {
                b.ToCouchbaseCollection(this, authorCollection);
                b.Property(a => a.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<ShadowBook>(b =>
            {
                b.ToCouchbaseCollection(this, bookCollection);
                b.Property(x => x.Id).ValueGeneratedNever();
                b.Property<int>("Rating");
                b.HasOne(x => x.Author).WithMany(a => a.Books);
                // The convention-named FK index embeds the dotted keyspace (IX_default.<scope>.<collection>_AuthorId),
                // which is not a valid index identifier, so the index is named explicitly.
                b.HasIndex("AuthorId").HasDatabaseName("ix_shadowbooks_authorid");
            });
        }
    }
}
