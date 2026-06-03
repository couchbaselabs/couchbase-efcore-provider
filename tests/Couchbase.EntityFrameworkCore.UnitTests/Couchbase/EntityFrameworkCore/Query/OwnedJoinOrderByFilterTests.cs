using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies that <c>ORDER BY</c> terms referencing suppressed owned-type JOIN aliases are
/// removed from the generated N1QL, so that no dangling alias references remain after
/// <c>VisitLeftJoin</c> / <c>VisitInnerJoin</c> suppress the corresponding JOIN clauses.
/// Covers depth-2 (Customer → Methods → Tags) and depth-3 (Customer → Methods → Tags → Audits)
/// OwnsMany chains to verify that <c>IsAllOwnedTablesSelect</c> recurses correctly.
/// </summary>
public class OwnedJoinOrderByFilterTests
{
    // -----------------------------------------------------------------------
    // Depth-2 model: Customer → ContactMethods → Tags
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
        public List<TagAudit> Audits { get; set; } = [];
    }

    // -----------------------------------------------------------------------
    // Depth-3 model: adds TagAudit nested inside ContactTag
    // Customer → ContactMethods → Tags → Audits
    // Exercises the recursive path in IsAllOwnedTablesSelect that was missing
    // before the fix — the lateral-join subquery for Audits is itself inside
    // the Tags subquery, requiring recursion to detect it as all-owned.
    // -----------------------------------------------------------------------

    private class TagAudit
    {
        public int Id { get; set; }
        public string Note { get; set; } = "";
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
                    cm.OwnsMany(m => m.Tags, t =>
                    {
                        t.HasKey(t => t.Id);
                        t.OwnsMany(t => t.Audits, a => a.HasKey(a => a.Id));
                    });
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
    // Helpers
    // -----------------------------------------------------------------------

    private static void AssertNoSuppressedAliasInOrderBy(string sql)
    {
        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        if (orderByIndex < 0) return;
        var orderByClause = sql[orderByIndex..];
        // No backtick-quoted alias referencing a suppressed owned-join table
        // should appear after ORDER BY.
        Assert.DoesNotContain("`s`.",  orderByClause, StringComparison.Ordinal);
        Assert.DoesNotContain("`s0`.", orderByClause, StringComparison.Ordinal);
        Assert.DoesNotContain("`s1`.", orderByClause, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Depth-2 tests (Customer → Methods → Tags)
    // -----------------------------------------------------------------------

    [Fact]
    public void Depth2_ToList_NoSuppressedAliasInOrderBy()
    {
        using var ctx = CreateContext();
        AssertNoSuppressedAliasInOrderBy(ctx.Customers.ToQueryString());
    }

    [Fact]
    public void Depth2_First_NoSuppressedAliasInOrderBy()
    {
        using var ctx = CreateContext();
        AssertNoSuppressedAliasInOrderBy(
            ctx.Customers.Where(c => c.CustomerId == 1).Take(1).ToQueryString());
    }

    [Fact]
    public void Depth2_ToList_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void Depth2_First_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.CustomerId == 1).Take(1).ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void Depth2_ToList_NoJoinEmitted()
    {
        using var ctx = CreateContext();
        Assert.DoesNotContain("JOIN", ctx.Customers.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Depth-3 tests (Customer → Methods → Tags → Audits)
    // These verify the recursive IsAllOwnedTablesSelect fix.
    // Before the fix, the TagAudit lateral-join subquery was not recognised as
    // all-owned, causing an empty FROM clause (N1QL error 3000) and a dangling
    // ORDER BY alias.
    // -----------------------------------------------------------------------

    [Fact]
    public void Depth3_ToList_NoSuppressedAliasInOrderBy()
    {
        using var ctx = CreateContext();
        AssertNoSuppressedAliasInOrderBy(ctx.Customers.ToQueryString());
    }

    [Fact]
    public void Depth3_First_NoSuppressedAliasInOrderBy()
    {
        using var ctx = CreateContext();
        AssertNoSuppressedAliasInOrderBy(
            ctx.Customers.Where(c => c.CustomerId == 1).Take(1).ToQueryString());
    }

    [Fact]
    public void Depth3_ToList_NoJoinEmitted()
    {
        using var ctx = CreateContext();
        Assert.DoesNotContain("JOIN", ctx.Customers.ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Depth3_ToList_NoEmptyFromClause()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.ToQueryString();
        // An empty FROM clause is the N1QL symptom of an unsuppressed owned-join
        // subquery with no tables (error 3000).  Verify it does not appear.
        Assert.DoesNotContain("FROM\n", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM\r\n", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Depth3_ToList_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void Depth3_First_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.CustomerId == 1).Take(1).ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }
}
