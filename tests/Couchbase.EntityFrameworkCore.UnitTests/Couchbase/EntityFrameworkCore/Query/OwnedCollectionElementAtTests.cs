using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies indexer/<c>.ElementAt()</c> access over a depth-1 <c>OwnsMany</c> navigation
/// (e.g. <c>customer.ContactMethods[0].Type</c>) translates to N1QL's native
/// <c>parentAlias.field[index].propertyName</c> subscript, not the correlated
/// OFFSET/LIMIT subquery EF Core builds by default -- which this provider's owned-table JOIN
/// suppression (<c>VisitTable</c>/<c>IsOwnedTable</c>) would otherwise turn into an
/// empty-FROM-clause N1QL error, the same bug class <c>.Any()</c> had.
/// </summary>
public class OwnedCollectionElementAtTests
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

    private class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "customer");
                b.HasKey(c => c.CustomerId);
                b.OwnsMany(c => c.ContactMethods, cm => cm.HasKey(m => m.Id));
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
    public void Indexer_InPredicate_TranslatesToNativeSubscript()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods[0].Type == "email").ToQueryString();

        Assert.Contains("`b`.`contactMethods`[0].`type` = 'email'", sql);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Indexer_Projected_TranslatesToNativeSubscript()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Select(c => c.ContactMethods[0].Type).ToQueryString();

        Assert.Contains("SELECT `b`.`contactMethods`[0].`type`", sql);
    }

    [Fact]
    public void ElementAt_WithNonZeroIndex_UsesThatSubscript()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods[3].Type == "email").ToQueryString();

        Assert.Contains("`b`.`contactMethods`[3].`type` = 'email'", sql);
    }

    [Fact]
    public void ElementAtOrDefault_TranslatesToSameNativeSubscript()
    {
        // N1QL's array subscript already returns MISSING (falsy) for an out-of-range index, so
        // ElementAtOrDefault needs no different rendering than the throwing ElementAt/indexer form.
        using var ctx = CreateContext();
        var sql = ctx.Customers.Select(c => c.ContactMethods.ElementAtOrDefault(0)!.Type).ToQueryString();

        Assert.Contains("SELECT `b`.`contactMethods`[0].`type`", sql);
    }

    [Fact]
    public void NonDefaultFieldNamingPolicy_AppliesToArrayFieldAndPropertyName()
    {
        using var ctx = CreateContext(JsonNamingPolicy.SnakeCaseLower);
        var sql = ctx.Customers.Where(c => c.ContactMethods[0].Type == "email").ToQueryString();

        Assert.Contains("`b`.`contact_methods`[0].`type` = 'email'", sql);
    }

    [Fact]
    public void WhereComposedBeforeElementAt_FailsTranslation_NotSilentlyInvalidSql()
    {
        // .Where(predicate).ElementAt(i) before the index is out of scope for v1 (see the plan's
        // scope notes). The correlation-stripping check rejects this shape (a non-null residual
        // predicate survives stripping) -- and, since the owned collection's TableExpression
        // renders as nothing (VisitTable), silently falling through to the generic subquery
        // rendering would produce an empty-FROM-clause N1QL parse error rather than a clear
        // translation-time failure. Must throw instead.
        using var ctx = CreateContext();
        Assert.Throws<NotSupportedException>(
            () => ctx.Customers
                .Select(c => c.ContactMethods.Where(m => m.Type == "x").ElementAt(0).Type)
                .ToQueryString());
    }

    // -------------------------------------------------------------------------
    // Regression: OwnsMany item type that itself table-splits a nested OwnsOne -- same concern
    // OwnedCollectionAnyTests guards for .Any(), applied here to indexer/.ElementAt().
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
    public void ItemTypeWithNestedOwnsOne_StillTranslatesToNativeSubscript()
    {
        using var ctx = CreateRichContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods[0].Type == "phone").ToQueryString();

        Assert.Contains("`b`.`contactMethods`[0].`type` = 'phone'", sql);
    }
}
