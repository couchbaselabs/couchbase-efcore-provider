using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies that EF Core's skip-navigation (HasMany/WithMany) queries generate
/// valid N1QL — no dangling aliases, balanced parentheses, no empty FROM, and
/// no owned-table JOIN leakage — before implementing Pattern B many-to-many support.
/// </summary>
public class SkipNavigationSqlGenerationTests
{
    private readonly ITestOutputHelper _output;
    public SkipNavigationSqlGenerationTests(ITestOutputHelper output) => _output = output;

    // -----------------------------------------------------------------------
    // Model — Post ↔ Tag via EF Core skip navigation (.HasMany().WithMany())
    // -----------------------------------------------------------------------

    private class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; } = "";
        public ICollection<Tag> Tags { get; set; } = [];
    }

    private class Tag
    {
        public string TagId { get; set; } = "";
        public string Name { get; set; } = "";
        public ICollection<Post> Posts { get; set; } = [];
    }

    private class BlogContext(DbContextOptions<BlogContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;
        public DbSet<Tag> Tags { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "post");
                b.HasKey(p => p.PostId);
                b.HasMany(p => p.Tags)
                 .WithMany(t => t.Posts)
                 .UsingEntity(j => j.ToTable("bucket.scope.posttag"));
            });

            modelBuilder.Entity<Tag>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "tag");
                b.HasKey(t => t.TagId);
            });
        }
    }

    private static BlogContext CreateContext()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<BlogContext>();
        builder.UseCouchbaseProvider(opts);
        return new BlogContext(builder.Options);
    }

    // -----------------------------------------------------------------------
    // SQL shape tests
    // -----------------------------------------------------------------------

    [Fact]
    public void SkipNav_Include_ToList_EmitsSQL()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Include(p => p.Tags).ToQueryString();
        _output.WriteLine(sql);
        Assert.NotEmpty(sql);
    }

    [Fact]
    public void SkipNav_Include_ToList_ContainsJoins()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Include(p => p.Tags).ToQueryString();
        // Skip navigation requires two JOINs: post→posttag and posttag→tag
        Assert.Contains("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkipNav_Include_ToList_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Include(p => p.Tags).ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void SkipNav_Include_First_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Include(p => p.Tags).Where(p => p.PostId == 1).Take(1).ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void SkipNav_Include_ToList_NoEmptyFromClause()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.Include(p => p.Tags).ToQueryString();
        Assert.DoesNotContain("FROM\n",   sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM\r\n", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkipNav_NoInclude_ToList_NoJoin()
    {
        using var ctx = CreateContext();
        var sql = ctx.Posts.ToQueryString();
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
}
