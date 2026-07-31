using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies `.Any(predicate)`/`.Any()` over a depth-1 <c>OwnsMany</c> navigation translates to
/// N1QL's <c>ANY x IN parentAlias.field SATISFIES ... END</c>, not the correlated <c>EXISTS</c>
/// subquery EF Core builds by default -- which this provider's owned-table JOIN suppression
/// (<c>VisitTable</c>/<c>IsOwnedTable</c>) would otherwise turn into an empty-FROM-clause N1QL
/// error, since there's no real keyspace to correlate against (the collection is a JSON array
/// field embedded in the parent document, already in scope).
/// </summary>
public class OwnedCollectionAnyTests
{
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
    }

    private class Order
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
    }

    private class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "customer");
                b.HasKey(c => c.CustomerId);
                b.OwnsMany(c => c.ContactMethods, cm => cm.HasKey(m => m.Id));
            });
            modelBuilder.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "order");
                b.HasKey(o => o.OrderId);
            });
        }
    }

    private static CustomerContext CreateContext(JsonNamingPolicy? fieldNamingPolicy = null)
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<CustomerContext>();
        builder.UseCouchbaseProvider(clusterOptions, o =>
        {
            if (fieldNamingPolicy != null)
            {
                o.FieldNamingPolicy = fieldNamingPolicy;
            }
        });
        return new CustomerContext(builder.Options);
    }

    [Fact]
    public void WithPredicate_TranslatesToAnySatisfies_NotExists()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Any(m => m.Type == "email")).ToQueryString();

        Assert.Contains("ANY `c` IN `b`.`contactMethods` SATISFIES `c`.`type` = 'email' END", sql);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase);

        // The correlation conjunct (CustomerId = CustomerId) must not leak into the WHERE clause
        // -- CustomerId legitimately appears elsewhere (the outer SELECT list, ORDER BY).
        var whereStart = sql.IndexOf("WHERE", StringComparison.Ordinal);
        var orderByStart = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        var whereClause = sql[whereStart..(orderByStart >= 0 ? orderByStart : sql.Length)];
        Assert.DoesNotContain("CustomerId", whereClause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPredicate_TranslatesToAnySatisfiesTrue()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Any()).ToQueryString();

        Assert.Contains("ANY `c` IN `b`.`contactMethods` SATISFIES true END", sql);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Negated_TranslatesToNotWrappedAnySatisfies()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => !c.ContactMethods.Any(m => m.Type == "email")).ToQueryString();

        Assert.Contains("NOT (ANY `c` IN `b`.`contactMethods` SATISFIES `c`.`type` = 'email' END)", sql);
    }

    [Fact]
    public void NonDefaultFieldNamingPolicy_AppliesToArrayFieldAndPropertyName()
    {
        using var ctx = CreateContext(JsonNamingPolicy.SnakeCaseLower);
        var sql = ctx.Customers.Where(c => c.ContactMethods.Any(m => m.Type == "email")).ToQueryString();

        Assert.Contains("`b`.`contact_methods`", sql);
        Assert.Contains("`c`.`type` = 'email'", sql);
    }

    [Fact]
    public void WithPredicate_ParenthesesBalanced()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Any(m => m.Type == "email")).ToQueryString();
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    [Fact]
    public void CorrelatedAnyOverUnrelatedDbSet_StillUsesExists()
    {
        // Control case: a genuine correlated .Any() whose subquery's sole table is NOT an owned
        // type (a manually-correlated subquery over an unrelated DbSet, not a navigation
        // expansion) must still render as a normal EXISTS -- proving GenerateExists's owned-table
        // detection doesn't over-fire and break this pre-existing, unrelated case.
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => ctx.Orders.Any(o => o.CustomerId == c.CustomerId)).ToQueryString();

        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ANY ", sql);
    }

    // -------------------------------------------------------------------------
    // Regression: OwnsMany item type that itself table-splits a nested OwnsOne (and/or hosts a
    // further nested OwnsMany) -- e.g. ContactMethod owning its own Label. The table backing such
    // an item type carries MORE THAN ONE owned IEntityType mapping (ContactMethod AND Label both
    // pass IsOwnedTable's All-owned check), and picking the wrong one resolves the wrong
    // ownership/FK, silently falling through to the broken default EXISTS rendering instead of
    // throwing -- caught empirically via a live-shaped integration-test model, not by static
    // reading alone.
    // -------------------------------------------------------------------------

    private class RichCustomer
    {
        public int CustomerId { get; set; }
        public List<RichContactMethod> ContactMethods { get; set; } = [];
    }

    private class RichContactMethod
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public ContactLabel? Label { get; set; }
    }

    private class ContactLabel
    {
        public string? DisplayName { get; set; }
    }

    private class RichCustomerContext(DbContextOptions<RichCustomerContext> options) : DbContext(options)
    {
        public DbSet<RichCustomer> Customers { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RichCustomer>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "customer");
                b.HasKey(c => c.CustomerId);
                b.OwnsMany(c => c.ContactMethods, cm =>
                {
                    cm.HasKey(m => m.Id);
                    cm.OwnsOne(m => m.Label);
                });
            });
        }
    }

    private static RichCustomerContext CreateRichContext()
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<RichCustomerContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new RichCustomerContext(builder.Options);
    }

    [Fact]
    public void ItemTypeWithNestedOwnsOne_StillTranslatesToAnySatisfies()
    {
        // Alias is `r` (from RichContactMethod), not `c` -- EF Core derives it from the CLR type
        // name of the single navigation source in scope.
        using var ctx = CreateRichContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Any(m => m.Type == "phone")).ToQueryString();

        Assert.Contains("ANY `r` IN `b`.`contactMethods` SATISFIES `r`.`type` = 'phone' END", sql);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItemTypeWithNestedOwnsOne_NoPredicate_StillTranslatesToAnySatisfies()
    {
        using var ctx = CreateRichContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Any()).ToQueryString();

        Assert.Contains("ANY `r` IN `b`.`contactMethods` SATISFIES true END", sql);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }
}
