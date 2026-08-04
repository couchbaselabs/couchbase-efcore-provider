using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <c>.All(predicate)</c> and <c>.Count(predicate)</c> over a depth-1 <c>OwnsMany</c>
/// navigation.
/// <para>
/// <c>.All(predicate)</c> needs no new production code: EF Core's own
/// <c>RelationalQueryableMethodTranslatingExpressionVisitor.TranslateAll</c> translates it as
/// <c>NOT EXISTS(... WHERE NOT predicate)</c> -- the exact same <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.ExistsExpression"/>
/// shape <c>.Any(predicate)</c> already produces, so it flows through the existing
/// <c>CouchbaseQuerySqlGenerator.GenerateExists</c> owned-collection detection for free.
/// </para>
/// <para>
/// <c>.Count(predicate)</c> needed a new <c>CouchbaseQuerySqlGenerator.TryRenderOwnedCollectionCount</c>
/// hook in <c>VisitScalarSubquery</c>, rendering
/// <c>(SELECT RAW COUNT(*) FROM parentAlias.field AS ownedAlias [WHERE predicate])[0]</c> --
/// mirroring the same bare-correlated-array-as-FROM-term shape already proven live for a scalar
/// primitive collection's <c>.Count</c>.
/// </para>
/// </summary>
public class OwnedCollectionAllCountTests
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

    // -------------------------------------------------------------------------
    // .All(predicate)
    // -------------------------------------------------------------------------

    [Fact]
    public void All_TranslatesToNegatedAnySatisfies_NotExists()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.All(m => m.Type == "email")).ToQueryString();

        Assert.Contains("NOT (ANY `c` IN `b`.`contactMethods` SATISFIES `c`.`type` <> 'email' END)", sql);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void All_NonDefaultFieldNamingPolicy_AppliesToArrayFieldAndPropertyName()
    {
        using var ctx = CreateContext(JsonNamingPolicy.SnakeCaseLower);
        var sql = ctx.Customers.Where(c => c.ContactMethods.All(m => m.Type == "email")).ToQueryString();

        Assert.Contains("`b`.`contact_methods`", sql);
        Assert.Contains("`c`.`type`", sql);
    }

    // -------------------------------------------------------------------------
    // .Count(predicate)
    // -------------------------------------------------------------------------

    [Fact]
    public void CountWithPredicate_TranslatesToCorrelatedCountSubquery()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Count(m => m.Type == "email") > 1).ToQueryString();

        Assert.Contains(
            "(SELECT RAW COUNT(*) FROM `b`.`contactMethods` AS `c` WHERE `c`.`type` = 'email')[0] > 1", sql);
    }

    [Fact]
    public void CountWithoutPredicate_OmitsWhereClause()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Count() > 1).ToQueryString();

        Assert.Contains("(SELECT RAW COUNT(*) FROM `b`.`contactMethods` AS `c`)[0] > 1", sql);
        Assert.DoesNotContain("WHERE `c`", sql);
    }

    [Fact]
    public void Count_NonDefaultFieldNamingPolicy_AppliesToArrayFieldAndPropertyName()
    {
        using var ctx = CreateContext(JsonNamingPolicy.SnakeCaseLower);
        var sql = ctx.Customers.Where(c => c.ContactMethods.Count(m => m.Type == "email") > 1).ToQueryString();

        Assert.Contains("`b`.`contact_methods`", sql);
        Assert.Contains("`c`.`type` = 'email'", sql);
    }

    [Fact]
    public void OtherAggregateOverOwnedCollection_FailsTranslation_NotSilentlyInvalidSql()
    {
        // .Sum()/.Max()/.Min()/.Average() over an OwnsMany navigation are out of scope -- only
        // .Any()/.All(predicate)/.Count(predicate)/indexer/.ElementAt() are supported. Must throw
        // a clear NotSupportedException rather than silently falling through to the generic
        // scalar-subquery rendering, which would produce an empty-FROM-clause N1QL parse error
        // (VisitTable renders the owned TableExpression as nothing).
        using var ctx = CreateContext();
        Assert.Throws<NotSupportedException>(
            () => ctx.Customers.Where(c => c.ContactMethods.Sum(m => m.Id) > 1).ToQueryString());
    }

    // -------------------------------------------------------------------------
    // Regression: OwnsMany item type that itself table-splits a nested OwnsOne -- same concern
    // OwnedCollectionAnyTests/OwnedCollectionElementAtTests guard for their own operators.
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
    public void ItemTypeWithNestedOwnsOne_All_StillTranslatesToNegatedAnySatisfies()
    {
        using var ctx = CreateRichContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.All(m => m.Type == "phone")).ToQueryString();

        Assert.Contains("NOT (ANY `r` IN `b`.`contactMethods` SATISFIES `r`.`type` <> 'phone' END)", sql);
    }

    [Fact]
    public void ItemTypeWithNestedOwnsOne_CountWithPredicate_StillTranslatesToCorrelatedCountSubquery()
    {
        using var ctx = CreateRichContext();
        var sql = ctx.Customers.Where(c => c.ContactMethods.Count(m => m.Type == "phone") > 0).ToQueryString();

        Assert.Contains(
            "(SELECT RAW COUNT(*) FROM `b`.`contactMethods` AS `r` WHERE `r`.`type` = 'phone')[0] > 0", sql);
    }
}
