using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Unit tests for the owned-collection clear logic in MaterializeOwnedItem.
/// These tests verify that the IList fast-path and the ICollection&lt;T&gt; fallback
/// both empty the collection correctly for all common mutable collection types.
/// </summary>
public class OwnedCollectionClearTests
{
    // ---------------------------------------------------------------------------
    // Helper — mirrors the exact clearing logic in MaterializeOwnedItem
    // ---------------------------------------------------------------------------
    private static void ClearOwnedCollection(object coll, Type elementType)
    {
        if (coll is IList list)
            list.Clear();
        else if (coll != null)
            typeof(ICollection<>).MakeGenericType(elementType)
                .GetMethod("Clear")!
                .Invoke(coll, null);
    }

    // ---------------------------------------------------------------------------
    // IList fast-path  (List<T>, ObservableCollection<T>, BindingList<T> …)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Clear_ListOfT_Empties()
    {
        var col = new List<string> { "a", "b", "c" };
        ClearOwnedCollection(col, typeof(string));
        Assert.Empty(col);
    }

    [Fact]
    public void Clear_ObservableCollection_Empties()
    {
        var col = new ObservableCollection<string> { "x", "y" };
        ClearOwnedCollection(col, typeof(string));
        Assert.Empty(col);
    }

    // ---------------------------------------------------------------------------
    // ICollection<T> fallback  (HashSet<T>, SortedSet<T> …)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Clear_HashSetOfT_Empties()
    {
        var col = new HashSet<string> { "a", "b", "c" };
        ClearOwnedCollection(col, typeof(string));
        Assert.Empty(col);
    }

    [Fact]
    public void Clear_SortedSetOfT_Empties()
    {
        var col = new SortedSet<string> { "alpha", "beta", "gamma" };
        ClearOwnedCollection(col, typeof(string));
        Assert.Empty(col);
    }

    // ---------------------------------------------------------------------------
    // Already-empty collections — must not throw
    // ---------------------------------------------------------------------------

    [Fact]
    public void Clear_EmptyList_DoesNotThrow()
    {
        var col = new List<int>();
        var ex = Record.Exception(() => ClearOwnedCollection(col, typeof(int)));
        Assert.Null(ex);
    }

    [Fact]
    public void Clear_EmptyHashSet_DoesNotThrow()
    {
        var col = new HashSet<int>();
        var ex = Record.Exception(() => ClearOwnedCollection(col, typeof(int)));
        Assert.Null(ex);
    }

    // ---------------------------------------------------------------------------
    // Null guard — must not throw
    // ---------------------------------------------------------------------------

    [Fact]
    public void Clear_NullCollection_DoesNotThrow()
    {
        var ex = Record.Exception(() => ClearOwnedCollection(null!, typeof(string)));
        Assert.Null(ex);
    }
}

public class CouchbaseQueryEnumerableTests
{
    [Fact]
    public void TryGetPropertyCI_finds_camelCase_property_case_insensitively()
    {
        var json = JsonDocument.Parse(
            """{"contactMethods":[{"id":1,"type":"email","value":"a@b.com"}]}""");

        // Search with PascalCase (as EF Core navigation name)
        var found = json.RootElement.TryGetPropertyCI("ContactMethods", out var val);

        Assert.True(found);
        Assert.Equal(JsonValueKind.Array, val.ValueKind);
        Assert.Equal(1, val.GetArrayLength());
    }

    [Theory]
    [InlineData("\"hello\"")]   // String
    [InlineData("42")]          // Number
    [InlineData("true")]        // Boolean
    [InlineData("null")]        // Null
    [InlineData("[1,2,3]")]     // Array
    public void TryGetPropertyCI_nonObject_returnsFalse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var found = doc.RootElement.TryGetPropertyCI("anything", out var val);
        Assert.False(found);
        Assert.Equal(JsonValueKind.Undefined, val.ValueKind);
    }
}

public class NestedOwnedSqlGenerationTests
{
    // ---- ToList (no subquery) ----
    [Fact]
    public void NestedOwnsMany_ToList_GeneratesValidN1QL()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.AsQueryable().ToQueryString();
        // Owned types are embedded JSON — no JOIN should be emitted.
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        // Parentheses must be balanced (catches stray-paren regressions regardless of formatting).
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    // ---- First (subquery with LIMIT 1) ----
    [Fact]
    public void NestedOwnsMany_First_GeneratesValidN1QL()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.Where(c => c.CustomerId == 1).Take(1).AsQueryable().ToQueryString();
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
    }

    private static CustomerContext CreateContext()
    {
        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CustomerContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new CustomerContext(builder.Options);
    }

    private class Customer
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = "";
        public Address Address { get; set; } = new();
        public List<ContactMethod> ContactMethods { get; set; } = [];
    }
    private class Address { public string? Street { get; set; } public string? City { get; set; } }
    private class ContactMethod
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public ContactLabel? Label { get; set; }
        public List<ContactTag> Tags { get; set; } = [];
    }
    private class ContactLabel { public string? DisplayName { get; set; } }
    private class ContactTag
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string Val { get; set; } = "";
    }

    private class CustomerContext(Microsoft.EntityFrameworkCore.DbContextOptions<CustomerContext> options)
        : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        public Microsoft.EntityFrameworkCore.DbSet<Customer> Customers { get; set; } = null!;
        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "customer");
                b.HasKey(c => c.CustomerId);
                b.OwnsOne(c => c.Address);
                b.OwnsMany(c => c.ContactMethods, cm =>
                {
                    cm.HasKey(m => m.Id);
                    cm.OwnsOne(m => m.Label);
                    cm.OwnsMany(m => m.Tags, t => t.HasKey(tg => tg.Id));
                });
            });
        }
    }
}
