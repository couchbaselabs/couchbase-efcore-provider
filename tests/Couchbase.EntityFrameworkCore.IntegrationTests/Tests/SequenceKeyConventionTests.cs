// Integer keys use a Couchbase sequence by default (CouchbaseKeySequenceConvention). Add gives
// the key a temporary value with no I/O, so a synchronous Add works; SaveChangesAsync fetches the
// real values as store-generated values, one query per sequence, and EF Core fixes up the
// dependents' foreign keys.
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class SequenceKeyConventionTests(BloggingFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        foreach (var collection in new[] { "seqkey_blogs", "seqkey_posts", "seqkey_unsequenced", "seqkey_guarded", "seqkey_blogtags" })
        {
            await ExecuteAsync(context,
                $"CREATE COLLECTION `{fixture.BucketName}`.`{fixture.ScopeName}`.`{collection}` IF NOT EXISTS");
            // A new collection takes a moment to become visible to the query service.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await ExecuteAsync(context,
                        $"CREATE PRIMARY INDEX IF NOT EXISTS ON `{fixture.BucketName}`.`{fixture.ScopeName}`.`{collection}`");
                    break;
                }
                catch (global::Couchbase.Core.Exceptions.IndexFailureException) when (attempt < 20)
                {
                    await Task.Delay(500);
                }
            }
        }

        foreach (var sequence in new[] { "SeqKeyBlogSequence", "SeqKeyPostSequence", "SeqKeyGuardedSequence" })
        {
            await ExecuteAsync(context,
                $"CREATE SEQUENCE IF NOT EXISTS `{fixture.BucketName}`.`{fixture.ScopeName}`.`{sequence}` START WITH 1");
        }

    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Synchronous_Add_then_SaveChangesAsync_assigns_keys_from_the_sequence()
    {
        await using var context = CreateContext();
        var blogs = Enumerable.Range(0, 3).Select(i => new SeqKeyBlog { Name = $"blog {i}" }).ToList();
        foreach (var blog in blogs)
        {
            context.Add(blog); // no sync opt-in needed: Add does no I/O
        }

        Assert.All(blogs, b => Assert.True(context.Entry(b).Property(e => e.Id).IsTemporary));

        await context.SaveChangesAsync();

        var ids = blogs.Select(b => b.Id).ToList();
        Assert.All(ids, id => Assert.True(id > 0, $"expected a sequence value, got {id}"));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(blogs, b => Assert.False(context.Entry(b).Property(e => e.Id).IsTemporary));

        await using var reader = CreateContext();
        foreach (var blog in blogs)
        {
            var loaded = await reader.Blogs.SingleAsync(b => b.Id == blog.Id);
            Assert.Equal(blog.Name, loaded.Name);
        }
    }

    [Fact]
    public async Task Dependents_get_the_principal_key_from_the_sequence()
    {
        await using var context = CreateContext();
        var blog = new SeqKeyBlog
        {
            Name = "with posts",
            Posts = [new SeqKeyPost { Title = "one" }, new SeqKeyPost { Title = "two" }],
        };
        context.Add(blog);

        await context.SaveChangesAsync();

        Assert.True(blog.Id > 0);
        Assert.All(blog.Posts, p =>
        {
            Assert.True(p.Id > 0);
            Assert.Equal(blog.Id, p.BlogId);
        });

        await using var reader = CreateContext();
        var titles = await reader.Posts.Where(p => p.BlogId == blog.Id).Select(p => p.Title).ToListAsync();
        Assert.Equal(["one", "two"], titles.Order());
    }

    [Fact]
    public async Task A_composite_key_gets_the_principal_key_from_the_sequence()
    {
        // The dependent's key includes the foreign key, which EF Core sets from the principal's
        // sequence value during the save: the document key must use it, not the CLR default.
        await using var context = CreateContext();
        var blog = new SeqKeyBlog { Name = "tagged" };
        context.Add(blog);
        context.Add(new SeqKeyBlogTag { Blog = blog, Tag = "a" });
        context.Add(new SeqKeyBlogTag { Blog = blog, Tag = "b" });

        await context.SaveChangesAsync();

        // The document IDs, not just the documents' BlogId fields, carry the sequence value.
        await using var reader = CreateContext();
        var ids = System.Text.Json.JsonSerializer.Deserialize<string[]>((string)(await ScalarAsync(reader,
            $"SELECT RAW ENCODE_JSON(ARRAY_AGG(META(t).id)) FROM `{fixture.BucketName}`.`{fixture.ScopeName}`.`seqkey_blogtags` t "
            + $"WHERE t.BlogId = {blog.Id}"))!)!;
        Assert.Equal(2, ids.Length);
        Assert.All(ids, id => Assert.Contains(blog.Id.ToString(), id));
    }

    [Fact]
    public async Task A_key_the_application_sets_is_kept()
    {
        var id = Random.Shared.Next(1_000_000_000, int.MaxValue);
        await using var context = CreateContext();
        context.Add(new SeqKeyBlog { Id = id, Name = "explicit" });

        await context.SaveChangesAsync();

        await using var reader = CreateContext();
        Assert.Equal("explicit", (await reader.Blogs.SingleAsync(b => b.Id == id)).Name);
    }

    [Fact]
    public async Task A_key_that_is_read_only_before_save_gets_a_sequence_value()
    {
        // The value is store-generated, like an identity column's, so EF Core's check for a
        // read-only key with an explicit value does not fire.
        await using var context = CreateContext();
        var guarded = new SeqKeyGuarded { Name = "guarded" };
        context.Add(guarded);

        await context.SaveChangesAsync();

        Assert.True(guarded.Id > 0);
        await using var reader = CreateContext();
        Assert.Equal("guarded", (await reader.Guarded.SingleAsync(g => g.Id == guarded.Id)).Name);
    }

    [Fact]
    public async Task A_missing_sequence_gives_an_error_naming_the_statement_to_run()
    {
        await using var context = CreateContext();
        var entity = new SeqKeyUnsequenced { Name = "never saved" };
        context.Add(entity);

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("SeqKeyUnsequencedSequence", e.Message);
        Assert.Contains("CREATE SEQUENCE", e.Message);
        Assert.Contains("EnsureCreatedAsync", e.Message);
        // The failed save keeps the temporary value, so a retry fetches a new one.
        Assert.True(context.Entry(entity).Property(x => x.Id).IsTemporary);
    }

    private SeqKeyContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SeqKeyContext>();
        options.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return new SeqKeyContext(options.Options);
    }

    private static async Task<object?> ScalarAsync(DbContext context, string statement)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(DbContext context, string statement)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync();
    }

    public class SeqKeyContext(DbContextOptions<SeqKeyContext> options) : DbContext(options)
    {
        public DbSet<SeqKeyBlog> Blogs => Set<SeqKeyBlog>();
        public DbSet<SeqKeyPost> Posts => Set<SeqKeyPost>();
        public DbSet<SeqKeyUnsequenced> Unsequenced => Set<SeqKeyUnsequenced>();
        public DbSet<SeqKeyGuarded> Guarded => Set<SeqKeyGuarded>();
        public DbSet<SeqKeyBlogTag> BlogTags => Set<SeqKeyBlogTag>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SeqKeyBlog>().ToCouchbaseCollection(this, "seqkey_blogs");
            modelBuilder.Entity<SeqKeyBlog>().HasMany(b => b.Posts).WithOne().HasForeignKey(p => p.BlogId);
            modelBuilder.Entity<SeqKeyPost>().ToCouchbaseCollection(this, "seqkey_posts");
            modelBuilder.Entity<SeqKeyUnsequenced>().ToCouchbaseCollection(this, "seqkey_unsequenced");
            modelBuilder.Entity<SeqKeyGuarded>().ToCouchbaseCollection(this, "seqkey_guarded");
            modelBuilder.Entity<SeqKeyBlogTag>().ToCouchbaseCollection(this, "seqkey_blogtags");
            modelBuilder.Entity<SeqKeyBlogTag>().HasKey(t => new { t.BlogId, t.Tag });
            modelBuilder.Entity<SeqKeyBlogTag>().HasOne(t => t.Blog).WithMany().HasForeignKey(t => t.BlogId);
            modelBuilder.Entity<SeqKeyGuarded>().Property(e => e.Id).Metadata
                .SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        }
    }

    public class SeqKeyBlog
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<SeqKeyPost> Posts { get; set; } = [];
    }

    public class SeqKeyPost
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public string? Title { get; set; }
    }

    public class SeqKeyUnsequenced
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class SeqKeyBlogTag
    {
        public int BlogId { get; set; }
        public string Tag { get; set; } = "";
        public SeqKeyBlog? Blog { get; set; }
    }

    public class SeqKeyGuarded
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
