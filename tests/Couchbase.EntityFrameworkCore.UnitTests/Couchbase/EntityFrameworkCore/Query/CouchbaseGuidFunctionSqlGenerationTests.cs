using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <see cref="Guid.NewGuid"/> translates to N1QL's <c>UUID()</c> (CBEF-23).
/// </summary>
public class CouchbaseGuidFunctionSqlGenerationTests
{
    [Fact]
    public void NewGuid_TranslatesToUuid()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Select(p => Guid.NewGuid());

        var sql = query.ToQueryString();

        Assert.Contains("UUID()", sql, StringComparison.OrdinalIgnoreCase);
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
