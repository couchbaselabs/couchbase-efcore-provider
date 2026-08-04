using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <c>.Any(predicate)</c>/<c>.All(predicate)</c>/<c>.Count(predicate)</c> and an
/// indexer access, when the target <c>OwnsMany</c> collection is reached through ANOTHER owned
/// navigation (depth &gt; 1, e.g. <c>customer.ContactMethods.Any(cm =&gt; cm.Tags.Any(...))</c>).
/// <para>
/// None of this needed new production code: <c>CouchbaseQuerySqlGenerator.GenerateExists</c>'s
/// <c>Visit(residual)</c> call and <c>TryRenderOwnedCollectionCount</c>'s equivalent naturally
/// recurse back into the same owned-collection detection logic for the inner shape, because
/// <c>TryGetOwnedEntityTypes</c>/<c>TryStripCorrelation</c> are written generically per-owned-type
/// (not hardcoded to the top-level navigation) and are alias-parameterized rather than
/// identity-tied to a specific table alias.
/// </para>
/// <para>
/// A genuinely DIFFERENT shape -- a direct chained indexer with no <c>.Any()</c>/<c>.All()</c>/
/// <c>.Count()</c> wrapping it (e.g. <c>c.ContactMethods[0].Tags[0].Key</c>) -- fails inside EF
/// Core's own core query-translation layer, before any Couchbase provider code runs. See
/// <see cref="OwnedCollectionChainedIndexerTests"/> for that (documented, not fixable here) case.
/// </para>
/// </summary>
public class OwnedCollectionNestedTests
{
    private class Customer
    {
        public int CustomerId { get; set; }
        public List<ContactMethod> ContactMethods { get; set; } = [];
    }

    private class ContactMethod
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
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
                    cm.OwnsMany(m => m.Tags, t => t.HasKey(x => x.Id));
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

    [Fact]
    public void NestedAny_TranslatesToNestedAnySatisfies()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .Where(c => c.ContactMethods.Any(cm => cm.Tags.Any(t => t.Key == "priority")))
            .ToQueryString();

        Assert.Contains(
            "WHERE ANY `c` IN `b`.`contactMethods` SATISFIES ANY `c0` IN `c`.`tags` SATISFIES `c0`.`key` = 'priority' END END",
            sql);
    }

    [Fact]
    public void NestedAll_TranslatesToNestedNegatedAnySatisfies()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .Where(c => c.ContactMethods.Any(cm => cm.Tags.All(t => t.Key == "priority")))
            .ToQueryString();

        Assert.Contains(
            "WHERE ANY `c` IN `b`.`contactMethods` SATISFIES NOT (ANY `c0` IN `c`.`tags` SATISFIES `c0`.`key` <> 'priority' END) END",
            sql);
    }

    [Fact]
    public void NestedCountWithPredicate_TranslatesToNestedCorrelatedCountSubquery()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .Where(c => c.ContactMethods.Any(cm => cm.Tags.Count(t => t.Key == "priority") > 0))
            .ToQueryString();

        Assert.Contains(
            "WHERE ANY `c` IN `b`.`contactMethods` SATISFIES (SELECT RAW COUNT(*) FROM `c`.`tags` AS `c0` WHERE `c0`.`key` = 'priority')[0] > 0 END",
            sql);
    }

    [Fact]
    public void OuterAll_InnerAny_TranslatesToNestedNegation()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .Where(c => c.ContactMethods.All(cm => cm.Tags.Any(t => t.Key == "priority")))
            .ToQueryString();

        Assert.Contains(
            "WHERE NOT (ANY `c` IN `b`.`contactMethods` SATISFIES NOT (ANY `c0` IN `c`.`tags` SATISFIES `c0`.`key` = 'priority' END) END)",
            sql);
    }

    [Fact]
    public void OuterAny_InnerIndexer_TranslatesToArraySubscriptInsideSatisfies()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .Where(c => c.ContactMethods.Any(cm => cm.Tags[0].Key == "priority"))
            .ToQueryString();

        Assert.Contains(
            "WHERE ANY `c` IN `b`.`contactMethods` SATISFIES `c`.`tags`[0].`key` = 'priority' END",
            sql);
    }

    [Fact]
    public void NegatedNestedAny_TranslatesToNotWrappingNestedAnySatisfies()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers
            .Where(c => !c.ContactMethods.Any(cm => cm.Tags.Any(t => t.Key == "priority")))
            .ToQueryString();

        Assert.Contains(
            "WHERE NOT (ANY `c` IN `b`.`contactMethods` SATISFIES ANY `c0` IN `c`.`tags` SATISFIES `c0`.`key` = 'priority' END END)",
            sql);
    }
}

/// <summary>
/// A direct chained indexer through two levels of <c>OwnsMany</c> with no
/// <c>.Any()</c>/<c>.All()</c>/<c>.Count()</c> wrapping it (e.g.
/// <c>c.ContactMethods[0].Tags[0].Key</c>) is a genuinely different shape from
/// <see cref="OwnedCollectionNestedTests"/>'s cases: it fails inside EF Core's own core
/// query-translation layer with <see cref="InvalidOperationException"/>, before any Couchbase
/// provider code runs -- the same class of EF-Core-level limitation already documented for
/// <c>.Contains()</c> over an <c>OwnsMany</c> navigation. Not fixable in this provider's SQL
/// generator. <c>.Any(predicate)</c> wrapping an inner indexer (see
/// <see cref="OwnedCollectionNestedTests.OuterAny_InnerIndexer_TranslatesToArraySubscriptInsideSatisfies"/>)
/// is the supported alternative.
/// </summary>
public class OwnedCollectionChainedIndexerTests
{
    private class Customer
    {
        public int CustomerId { get; set; }
        public List<ContactMethod> ContactMethods { get; set; } = [];
    }

    private class ContactMethod
    {
        public int Id { get; set; }
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
                    cm.OwnsMany(m => m.Tags, t => t.HasKey(x => x.Id));
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

    [Fact]
    public void ChainedIndexerInWhere_FailsEfCoreTranslation_NotSilentlyWrongSql()
    {
        using var ctx = CreateContext();
        Assert.Throws<InvalidOperationException>(
            () => ctx.Customers.Where(c => c.ContactMethods[0].Tags[0].Key == "priority").ToQueryString());
    }

    [Fact]
    public void ChainedIndexerInSelect_FailsEfCoreTranslation_NotSilentlyWrongSql()
    {
        using var ctx = CreateContext();
        Assert.Throws<InvalidOperationException>(
            () => ctx.Customers.Select(c => c.ContactMethods[0].Tags[0].Key).ToQueryString());
    }
}
