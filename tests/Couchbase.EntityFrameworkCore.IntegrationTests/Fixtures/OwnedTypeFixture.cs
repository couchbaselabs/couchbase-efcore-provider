using Couchbase.EntityFrameworkCode.IntegrationTests.Models;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;

public class OwnedTypeFixture : CouchbaseFixture<OwnedTypeDbContext>
{
    public override string ScopeName { get; } = "ownedtypes";

    public override OwnedTypeDbContext GetDbContext()
        => new OwnedTypeDbContext(CreateDbContextOptions<OwnedTypeDbContext>());

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var ctx = GetDbContext();
        await ctx.Database.EnsureCreatedAsync();
        await LoadDataAsync();
    }

    public override async Task LoadDataAsync()
    {
        await using var ctx = GetDbContext();
        // Use UpdateRange (→ UpsertAsync) so this is idempotent whether or not the
        // documents already exist from a previous test run or a previous test in this run.
        ctx.UpdateRange(
            new Customer
            {
                CustomerId = 1,
                Name = "Alice",
                Address = new Address { Street = "1 Main St", City = "Springfield" },
                ContactMethods =
                [
                    new ContactMethod { Id = 1, Type = "email", Value = "alice@example.com" },
                    new ContactMethod { Id = 2, Type = "phone", Value = "555-0100" }
                ]
            },
            new Customer
            {
                CustomerId = 2,
                Name = "Bob",
                Address = new Address { Street = "2 Elm St", City = "Shelbyville" },
                ContactMethods =
                [
                    new ContactMethod { Id = 1, Type = "email", Value = "bob@example.com" }
                ]
            },
            new Customer
            {
                CustomerId = 3,
                Name = "Carol",
                Address = new Address { Street = "3 Oak Ave", City = "Shelbyville" },
                ContactMethods =
                [
                    new ContactMethod
                    {
                        Id = 1,
                        Type = "email",
                        Value = "carol@example.com",
                        Label = new ContactLabel { DisplayName = "Work Email" },
                        Tags =
                        [
                            new ContactTag
                            {
                                Id = 1, Key = "priority", Val = "high",
                                Audits =
                                [
                                    new TagAudit { Id = 1, Note = "set by admin" },
                                    new TagAudit { Id = 2, Note = "confirmed" }
                                ]
                            },
                            new ContactTag { Id = 2, Key = "verified", Val = "true", Audits = [] }
                        ]
                    },
                    new ContactMethod
                    {
                        Id = 2,
                        Type = "phone",
                        Value = "555-0303",
                        Label = new ContactLabel { DisplayName = "Mobile" },
                        Tags = []
                    }
                ]
            });
        await ctx.SaveChangesAsync();
    }

    public class Customer
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = "";
        public Address Address { get; set; } = new();
        public List<ContactMethod> ContactMethods { get; set; } = [];
    }

    public class Address
    {
        public string? Street { get; set; }
        public string? City { get; set; }
    }

    public class ContactMethod
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public ContactLabel? Label { get; set; }
        public List<ContactTag> Tags { get; set; } = [];
    }

    public class ContactLabel
    {
        public string? DisplayName { get; set; }
    }

    public class ContactTag
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
        public string Val { get; set; } = "";
        /// <summary>
        /// Depth-3 OwnsMany — exercises the recursive <c>IsAllOwnedTablesSelect</c> path.
        /// </summary>
        public List<TagAudit> Audits { get; set; } = [];
    }

    /// <summary>
    /// Owned entity at depth 3 (Customer → ContactMethod → ContactTag → TagAudit).
    /// Used to verify that <c>IsAllOwnedTablesSelect</c> recurses correctly and does
    /// not leave an empty FROM clause or dangling ORDER BY alias in the generated N1QL.
    /// </summary>
    public class TagAudit
    {
        public int Id { get; set; }
        public string Note { get; set; } = "";
    }

    // -------------------------------------------------------------------------
    // HashSet<T>-backed model — used by OwnedCollectionClearIntegrationTests
    // -------------------------------------------------------------------------

    /// <summary>
    /// An entity whose OwnsMany navigation uses HashSet&lt;T&gt; instead of List&lt;T&gt;.
    /// This exercises the ICollection&lt;T&gt; fallback clear path in MaterializeOwnedItem.
    /// </summary>
    public class HashSetCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>Intentionally HashSet, not List — tests the non-IList clear path.</summary>
        public HashSet<HashSetTag> Tags { get; set; } = [];
    }

    public class HashSetTag
    {
        public int Id { get; set; }
        public string Value { get; set; } = "";
    }
}
