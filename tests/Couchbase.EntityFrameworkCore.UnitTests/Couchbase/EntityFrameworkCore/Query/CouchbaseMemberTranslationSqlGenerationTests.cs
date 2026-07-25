using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies LINQ property/member translation to SQL++ (CBEF-23), via the new
/// <c>CouchbaseMemberTranslatorProvider</c> pipeline (previously nonexistent in this provider --
/// properties like <c>string.Length</c> go through <c>IMemberTranslator</c>, not
/// <c>IMethodCallTranslator</c>, which only handles method calls like <c>.ToLower()</c>).
/// </summary>
public class CouchbaseMemberTranslationSqlGenerationTests
{
    [Fact]
    public void StringLength_TranslatesToLength()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => p.Title.Length > 5);

        var sql = query.ToQueryString();

        Assert.Contains("LENGTH(", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static PostContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<PostContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new PostContext(builder.Options);
    }

    private class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; } = null!;
    }

    private class PostContext(DbContextOptions<PostContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "post");
                b.HasKey(p => p.PostId);
            });
        }
    }
}
