using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

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
    [Fact]
    public void NestedOwnsMany_ToList_GeneratesValidN1QL()
    {
        using var ctx = CreateContext();
        var sql = ctx.Customers.AsQueryable().ToQueryString();
        // Must not contain aliases from skipped owned JOINs
        Assert.DoesNotContain("cm0", sql);
        Assert.DoesNotContain("ct0", sql);
        // Must not have a stray closing paren at start of a line
        foreach (var line in sql.Split('\n'))
            Assert.False(line.TrimStart().StartsWith(")") && line.Trim().Length == 1,
                $"Stray closing paren found in N1QL:\n{sql}");
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

    // ---- ToList (no subquery) ----
    [Fact]
    public void NestedOwnsMany_WithOwnsOneRoot_ToList_GeneratesValidN1QL()
    {
        using var ctx = CreateContextWithOwnsOne();
        var sql = ctx.Customers.AsQueryable().ToQueryString();
        Assert.DoesNotContain("cm0", sql);
        Assert.DoesNotContain("ct0", sql);
        foreach (var line in sql.Split('\n'))
            Assert.False(line.TrimStart() == ")", $"Stray ) found:\n{sql}");
    }

    // ---- First (subquery with LIMIT 1) ----
    [Fact]
    public void NestedOwnsMany_WithOwnsOneRoot_First_GeneratesValidN1QL()
    {
        using var ctx = CreateContextWithOwnsOne();
        var sql = ctx.Customers.Where(c => c.CustomerId == 1).Take(1).AsQueryable().ToQueryString();
        Assert.DoesNotContain("cm0", sql);
        Assert.DoesNotContain("ct0", sql);
        foreach (var line in sql.Split('\n'))
            Assert.False(line.TrimStart() == ")", $"Stray ) found:\n{sql}");
    }

    private static CustomerContext CreateContextWithOwnsOne()
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
