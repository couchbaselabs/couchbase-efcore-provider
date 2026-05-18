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
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
    }

    public class ContactMethod
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
