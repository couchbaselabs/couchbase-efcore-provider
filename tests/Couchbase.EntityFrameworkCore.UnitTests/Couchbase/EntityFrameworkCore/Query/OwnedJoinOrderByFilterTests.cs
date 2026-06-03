using System.Text.RegularExpressions;
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

    /// <summary>
    /// Asserts that every table alias referenced in the ORDER BY clause is also
    /// defined as a live alias in the FROM/JOIN clauses of the same SQL statement.
    /// This is alias-name-agnostic — it does not hard-code "s", "s0", etc., so it
    /// correctly catches any dangling suppressed alias regardless of what EF assigns.
    /// </summary>
    private static void AssertNoSuppressedAliasInOrderBy(string sql)
    {
        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        if (orderByIndex < 0) return;

        // Collect every alias defined in FROM/AS and JOIN/AS clauses before ORDER BY.
        // Pattern: AS `alias`  (EF always uses backtick-quoted aliases)
        var fromSection = sql[..orderByIndex];
        var definedAliases = new HashSet<string>(
            Regex.Matches(fromSection, @"AS\s+`([^`]+)`")
                 .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        // Collect every alias *used* in ORDER BY: `alias`.`column`
        var orderByClause = sql[orderByIndex..];
        var usedAliases = Regex.Matches(orderByClause, @"`([^`]+)`\.")
                               .Select(m => m.Groups[1].Value)
                               .Distinct();

        foreach (var alias in usedAliases)
            Assert.True(definedAliases.Contains(alias),
                $"ORDER BY references alias `{alias}` which is not defined in FROM/JOIN — dangling suppressed alias.");
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

    // -----------------------------------------------------------------------
    // EmitOrdering — verifies that the extracted helper correctly emits
    // ASC / DESC and that surviving owner-column orderings are preserved
    // after the suppressed-alias filter runs.
    // -----------------------------------------------------------------------

    [Fact]
    public void EmitOrdering_AscendingOrderBy_EmitsAsc()
    {
        // Default OrderBy is ascending — verify "ASC" appears in the ORDER BY clause.
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .OrderBy(c => c.CustomerId)
            .ToQueryString();

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        Assert.True(orderByIndex >= 0, "Expected an ORDER BY clause");
        Assert.Contains("ASC", sql[orderByIndex..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitOrdering_DescendingOrderBy_EmitsDesc()
    {
        // OrderByDescending — verify "DESC" appears in the ORDER BY clause.
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .OrderByDescending(c => c.CustomerId)
            .ToQueryString();

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        Assert.True(orderByIndex >= 0, "Expected an ORDER BY clause");
        Assert.Contains("DESC", sql[orderByIndex..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitOrdering_OwnerColumnPreserved_AfterSuppressedAliasFiltered()
    {
        // When suppressed-alias orderings are dropped, surviving owner-column
        // orderings must still appear with the correct direction.
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .OrderByDescending(c => c.CustomerId)
            .ToQueryString();

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        Assert.True(orderByIndex >= 0, "Expected an ORDER BY clause");
        var orderByClause = sql[orderByIndex..];

        // Owner column must be present.
        Assert.Contains("CustomerId", orderByClause, StringComparison.OrdinalIgnoreCase);
        // Direction must be DESC as requested.
        Assert.Contains("DESC", orderByClause, StringComparison.OrdinalIgnoreCase);
        // No suppressed alias must leak through.
        AssertNoSuppressedAliasInOrderBy(sql);
    }

    [Fact]
    public void EmitOrdering_MultipleOwnerColumns_AllPreserved()
    {
        // Multiple surviving ORDER BY terms must all appear — none should be dropped.
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .OrderBy(c => c.CustomerId)
            .ThenByDescending(c => c.Name)
            .ToQueryString();

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        Assert.True(orderByIndex >= 0, "Expected an ORDER BY clause");
        var orderByClause = sql[orderByIndex..];

        Assert.Contains("CustomerId", orderByClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name",       orderByClause, StringComparison.OrdinalIgnoreCase);
        // First column ASC, second DESC.
        Assert.Contains("ASC",  orderByClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", orderByClause, StringComparison.OrdinalIgnoreCase);
    }
}
