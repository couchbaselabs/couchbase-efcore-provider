using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <see cref="CouchbaseDbFunctionsExtensions"/>'s IsMissing/IsNotMissing/IsValued/
/// IsNotValued translate to N1QL's postfix <c>IS [NOT] MISSING</c>/<c>IS [NOT] VALUED</c>
/// operators via <c>CouchbaseMissingValuedMethodTranslator</c>.
/// </summary>
public class CouchbaseMissingValuedSqlGenerationTests
{
    [Fact]
    public void IsMissing_TranslatesToIsMissingPostfix()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.IsMissing(p.Score));

        var sql = query.ToQueryString();
        Assert.Contains("(`b`.`Score`) IS MISSING", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsNotMissing_TranslatesToIsNotMissingPostfix()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.IsNotMissing(p.Score));

        var sql = query.ToQueryString();
        Assert.Contains("(`b`.`Score`) IS NOT MISSING", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsValued_TranslatesToIsValuedPostfix()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.IsValued(p.Score));

        var sql = query.ToQueryString();
        Assert.Contains("(`b`.`Score`) IS VALUED", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsNotValued_TranslatesToIsNotValuedPostfix()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.IsNotValued(p.Score));

        var sql = query.ToQueryString();
        Assert.Contains("(`b`.`Score`) IS NOT VALUED", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsMissing_OnStringProperty_TranslatesCorrectly()
    {
        // Confirm the generic method works for a reference-typed property too, not just value
        // types -- exercises a different closed generic instantiation of IsMissing<T>.
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => EF.Functions.IsMissing(p.Title));

        var sql = query.ToQueryString();
        Assert.Contains("(`b`.`Title`) IS MISSING", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsMissing_Negated_TranslatesToNotWrappingPostfixExpression()
    {
        using var ctx = CreateContext();
        var query = ctx.Posts.Where(p => !EF.Functions.IsMissing(p.Score));

        var sql = query.ToQueryString();
        Assert.Contains("IS MISSING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IS NOT MISSING", sql, StringComparison.OrdinalIgnoreCase);
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
        public double Score { get; set; }
        public string? Title { get; set; }
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
