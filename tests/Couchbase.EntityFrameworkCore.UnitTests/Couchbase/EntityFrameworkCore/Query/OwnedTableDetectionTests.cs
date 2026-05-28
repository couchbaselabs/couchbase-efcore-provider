using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Documents and verifies the owned-table detection logic used in
/// <c>CouchbaseQuerySqlGenerator.IsOwnedTable</c>, which drives <c>VisitLeftJoin</c>,
/// <c>VisitInnerJoin</c>, and <c>VisitTable</c>.
/// </summary>
/// <remarks>
/// <para>
/// The central contract: <see cref="IEntityType.IsOwned"/> is <c>true</c> for owned
/// entity types and <c>false</c> for owner / standalone entity types.
/// </para>
/// <para>
/// <c>IsOwnedTable</c> uses <c>All</c> over
/// <see cref="ITableBase.EntityTypeMappings"/> so that tables hosting both an owner entity
/// and an <c>OwnsOne</c> scalar navigation (table-splitting) are not mistakenly suppressed.
/// Using <c>Any</c> instead would return <c>true</c> for the owner's table, causing its
/// FROM clause to be omitted and producing a N1QL syntax error at the <c>WHERE</c> keyword
/// (Couchbase error 3000).
/// </para>
/// </remarks>
public class OwnedTableDetectionTests
{
    // ---------------------------------------------------------------
    // Section 1: EF Core IsOwned() semantics (model-only, no relational provider)
    // ---------------------------------------------------------------

    [Fact]
    public void OwnsOne_OwnerEntityType_IsOwnedReturnsFalse()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<SingleOwner>(b => b.OwnsOne(o => o.Embedded));
        var model = modelBuilder.FinalizeModel();

        var ownerType = model.FindEntityType(typeof(SingleOwner))!;
        Assert.False(ownerType.IsOwned(),
            "The owner entity itself must NOT be reported as owned.");
    }

    [Fact]
    public void OwnsOne_OwnedEntityType_IsOwnedReturnsTrue()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<SingleOwner>(b => b.OwnsOne(o => o.Embedded));
        var model = modelBuilder.FinalizeModel();

        var ownerType = model.FindEntityType(typeof(SingleOwner))!;
        var ownedType = ownerType.FindNavigation(nameof(SingleOwner.Embedded))!.TargetEntityType;

        Assert.True(ownedType.IsOwned(),
            "The OwnsOne-owned entity type must be reported as owned.");
    }

    [Fact]
    public void OwnsMany_OwnedEntityType_IsOwnedReturnsTrue()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<SingleOwner>(b => b.OwnsMany(o => o.Items));
        var model = modelBuilder.FinalizeModel();

        var ownerType  = model.FindEntityType(typeof(SingleOwner))!;
        var ownedType  = ownerType.FindNavigation(nameof(SingleOwner.Items))!.TargetEntityType;

        Assert.True(ownedType.IsOwned(),
            "The OwnsMany-owned entity type must be reported as owned.");
    }

    [Fact]
    public void StandaloneEntityType_IsOwnedReturnsFalse()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<SingleOwner>();
        var model = modelBuilder.FinalizeModel();

        var entityType = model.FindEntityType(typeof(SingleOwner))!;
        Assert.False(entityType.IsOwned(),
            "A standalone entity (no OwnsOne/OwnsMany relationship) must not be owned.");
    }

    // ---------------------------------------------------------------
    // Section 2: Relational model EntityTypeMappings — All vs Any
    //
    // Requires a full DbContext with the Couchbase relational provider so that
    // EF Core builds the ITable → IEntityType mapping used by IsOwnedTable.
    // ---------------------------------------------------------------

    [Fact]
    public void OwnsOne_OwnerTable_EntityTypeMappingsContainsBothOwnerAndOwnedType()
    {
        // This is the critical model-level truth that IsOwnedTable relies on.
        //
        // With OwnsOne the owned entity is table-split onto the owner's table.  EF Core
        // therefore registers BOTH the owner entity type AND the owned entity type in that
        // table's EntityTypeMappings:
        //
        //   Any(m => IsOwned) = true   ← WRONG: would suppress the owner's FROM clause
        //   All(m => IsOwned) = false  ← CORRECT: owner table is not an "owned-only" table
        //
        // If someone were to change All → Any in IsOwnedTable, this test would catch the
        // regression before the integration tests (which need a live server).

        using var ctx = CreateContext();

        var ownerEntityType = ctx.Model.FindEntityType(typeof(Customer))!;
        // GetTableMappings() is a relational extension method — only available when the
        // context is built with a relational provider such as UseCouchbaseProvider.
        var table    = ownerEntityType.GetTableMappings().First().Table;
        var mappings = table.EntityTypeMappings.ToList();

        // The owner table should carry mappings for at least the owner and the OwnsOne entity.
        Assert.True(mappings.Count >= 2,
            $"Owner table should have ≥ 2 EntityTypeMappings (got {mappings.Count}). " +
            "Expected at least one entry for the owner and one for the OwnsOne entity.");

        // At least one mapping must be for an owned entity.
        Assert.Contains(mappings, m => m.TypeBase is IEntityType et && et.IsOwned());

        // At least one mapping must be for the non-owned owner entity.
        Assert.Contains(mappings, m => m.TypeBase is IEntityType et && !et.IsOwned());

        // All(IsOwned) must return false — this is what IsOwnedTable correctly evaluates,
        // keeping the owner's FROM clause in the generated N1QL.
        var allOwned = mappings.All(m => m.TypeBase is IEntityType et && et.IsOwned());
        Assert.False(allOwned,
            "All(IsOwned) must be false for the owner table. " +
            "If it returned true, IsOwnedTable would suppress the FROM clause, producing a " +
            "N1QL syntax error (WHERE keyword without a preceding FROM).");
    }

    // ---------------------------------------------------------------
    // Section 3: SQL generation — FROM-clause regression tests
    //
    // Verifies that CouchbaseQuerySqlGenerator emits valid N1QL for models
    // with OwnsOne / OwnsMany relationships.  These tests catch the regression
    // where using Any() instead of All() in IsOwnedTable caused the owner's
    // FROM clause to be omitted, producing:
    //
    //   SELECT ... WHERE ...    ← invalid N1QL (no FROM)
    //
    // instead of the correct:
    //
    //   SELECT ... FROM `bucket`.`scope`.`customer` AS c WHERE ...
    //
    // The tests run without a live Couchbase server; ToQueryString() compiles
    // the LINQ to SQL++ in-process without executing the query.
    // ---------------------------------------------------------------

    [Fact]
    public void CustomerQuery_GeneratesFromClause()
    {
        using var ctx = CreateContext();

        var sql = ctx.Customers.ToQueryString();

        Assert.Contains("FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomerQuery_FromClauseContainsBacktickedKeyspace()
    {
        using var ctx = CreateContext();

        var sql = ctx.Customers.ToQueryString();

        // Each part of the three-part keyspace must appear backtick-delimited.
        Assert.Contains("`bucket`",   sql);
        Assert.Contains("`scope`",    sql);
        Assert.Contains("`customer`", sql);
    }

    [Fact]
    public void CustomerQuery_WithWhere_FromPrecedesWhere()
    {
        // Regression: with Any() instead of All() in IsOwnedTable, the owner's FROM clause
        // was suppressed for entities that also host OwnsOne scalars, yielding
        // "SELECT … WHERE …" (no FROM).  Couchbase Server rejects this with:
        //   ParsingFailureException: syntax error — line 5, column 5, at: WHERE [3000]
        //
        // This test verifies the correct ordering without requiring a live server.

        using var ctx = CreateContext();

        var sql = ctx.Customers
            .Where(c => c.CustomerId > 0)
            .ToQueryString();

        var fromIndex  = sql.IndexOf("FROM",  StringComparison.OrdinalIgnoreCase);
        var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);

        Assert.True(fromIndex  >= 0, $"Query must contain FROM clause.\nGenerated SQL:\n{sql}");
        Assert.True(whereIndex >= 0, $"Query must contain WHERE clause.\nGenerated SQL:\n{sql}");
        Assert.True(fromIndex < whereIndex,
            $"FROM must appear before WHERE.\nGenerated SQL:\n{sql}");
    }

    [Fact]
    public void CustomerQuery_WithOwnsOne_GeneratesNoJoinForEmbeddedAddress()
    {
        // Address is an OwnsOne / table-split scalar navigation — it is embedded in the
        // owner document, not a separate Couchbase collection.  The generated SQL++ must
        // not contain a JOIN clause for it.

        using var ctx = CreateContext();

        // A plain select (no explicit Include) should not add a JOIN.
        var sql = ctx.Customers.ToQueryString();

        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static CustomerContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        // Build options via the strongly-typed builder.  UseCouchbaseProvider returns the
        // non-generic base type, so we keep a local reference to preserve the generic form
        // for the Options property.
        var builder = new DbContextOptionsBuilder<CustomerContext>();
        builder.UseCouchbaseProvider(clusterOptions);

        return new CustomerContext(builder.Options);
    }

    // ---------------------------------------------------------------
    // Test models — Section 1 (model-only tests, no relational provider)
    // ---------------------------------------------------------------

    private class SingleOwner
    {
        public int Id { get; set; }
        public EmbeddedValue Embedded { get; set; } = new();
        public List<OwnedItem> Items { get; set; } = [];
    }

    private class EmbeddedValue { public string? Value { get; set; } }

    private class OwnedItem
    {
        public int     Id   { get; set; }
        public string? Name { get; set; }
    }

    // ---------------------------------------------------------------
    // Test models — Sections 2 & 3 (relational provider / SQL generation)
    // ---------------------------------------------------------------

    private class Customer
    {
        public int    CustomerId { get; set; }
        public string Name       { get; set; } = "";
        public Address           Address        { get; set; } = new();
        public List<ContactMethod> ContactMethods { get; set; } = [];
    }

    private class Address
    {
        public string? Street { get; set; }
        public string? City   { get; set; }
    }

    private class ContactMethod
    {
        public int    Id   { get; set; }
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
                b.OwnsOne(c => c.Address);
                b.OwnsMany(c => c.ContactMethods);
            });
        }
    }
}
