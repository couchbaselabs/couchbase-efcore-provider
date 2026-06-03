using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies that <c>ORDER BY</c> terms referencing suppressed owned-type JOIN aliases are
/// removed from the generated N1QL, so that no dangling alias references remain after
/// <c>VisitLeftJoin</c> / <c>VisitInnerJoin</c> suppress the corresponding JOIN clauses.
/// </summary>
public class OwnedJoinOrderByFilterTests
{
    // -----------------------------------------------------------------------
    // Model
    // -----------------------------------------------------------------------

    private class Customer
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = "";
        public List<ContactMethod> ContactMethods { get; set; } = [];
    }

    private class ContactMethod
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public List<ContactTag> Tags { get; set; } = [];
    }

    private class ContactTag
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
    }

    private class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "customer");
                b.HasKey(c => c.CustomerId);
                b.OwnsMany(c => c.ContactMethods, cm =>
                {
                    cm.HasKey(m => m.Id);
                    cm.OwnsMany(m => m.Tags, t => t.HasKey(t => t.Id));
                });
            });
        }
    }

    private static CustomerContext CreateContext()
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<CustomerContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new CustomerContext(builder.Options);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ToList_NoSuppressedAliasInOrderBy()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.ToQueryString();

        // The suppressed owned-join alias (e.g. "s") must not appear in ORDER BY.
        // Owner ordering terms (e.g. d.customerId) are still permitted.
        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        if (orderByIndex < 0) return; // no ORDER BY at all — trivially correct

        var orderByClause = sql[orderByIndex..];
        // Owned-join lateral alias "s" should not appear in the ORDER BY clause.
        // (Owner alias "d" is fine.)
        Assert.DoesNotContain("`s`.", orderByClause, StringComparison.Ordinal);
    }

    [Fact]
    public void First_NoSuppressedAliasInOrderBy()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.CustomerId == 1).Take(1).ToQueryString();

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        if (orderByIndex < 0) return;

        var orderByClause = sql[orderByIndex..];
        Assert.DoesNotContain("`s`.", orderByClause, StringComparison.Ordinal);
    }

    [Fact]
    public void ToList_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void First_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.CustomerId == 1).Take(1).ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void ToList_NoJoinEmitted()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.ToQueryString();
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
}
