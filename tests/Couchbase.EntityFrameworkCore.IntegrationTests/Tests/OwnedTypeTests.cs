using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class OwnedTypeTests(
    OwnedTypeFixture fixture,
    ITestOutputHelper output) : IAsyncLifetime
{
    // Reseed before each test so mutation tests (Update, Clear) cannot affect read-only
    // tests regardless of the order xUnit chooses to run them.
    public Task InitializeAsync() => fixture.LoadDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task OwnsOne_InlineAddress_IsPopulated()
    {
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);

        Assert.NotNull(customer.Address);
        Assert.Equal("1 Main St", customer.Address.Street);
        Assert.Equal("Springfield", customer.Address.City);
    }

    [Fact]
    public async Task OwnsMany_EmbeddedContactMethods_ArePopulated()
    {
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);

        Assert.Equal(2, customer.ContactMethods.Count);
        Assert.Contains(customer.ContactMethods, cm => cm.Type == "email" && cm.Value == "alice@example.com");
        Assert.Contains(customer.ContactMethods, cm => cm.Type == "phone" && cm.Value == "555-0100");
    }

    [Fact]
    public async Task OwnsMany_SingleItem_IsPopulated()
    {
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);

        Assert.Single(customer.ContactMethods);
        Assert.Equal("bob@example.com", customer.ContactMethods[0].Value);
    }

    [Fact]
    public async Task Customers_AllHaveAddresses()
    {
        await using var ctx = fixture.GetDbContext();
        var customers = await ctx.Customers.ToListAsync();

        Assert.All(customers, c => Assert.NotNull(c.Address));
    }

    [Fact]
    public async Task OwnsOne_Update_RoundTrips()
    {
        // Write a new address via SaveChangesAsync and confirm a fresh context reads it back.
        // No manual State = Modified needed: the deferred second pass in SaveChangesAsync
        // detects the owned-entry state change and writes the owner document automatically.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            customer.Address.Street = "99 Updated Ln";
            customer.Address.City = "Shelbyville";
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            Assert.Equal("99 Updated Ln", customer.Address.Street);
            Assert.Equal("Shelbyville",   customer.Address.City);
        }
    }

    [Fact]
    public async Task OwnsOne_NullScalars_RoundTrip()
    {
        // Setting individual OwnsOne scalar properties to null must write
        // explicit null columns so the previously stored values are overwritten.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            customer.Address.Street = null;
            customer.Address.City = null;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            Assert.NotNull(customer.Address);
            Assert.Null(customer.Address.Street);
            Assert.Null(customer.Address.City);
        }
    }

    [Fact]
    public async Task OwnsMany_Update_RoundTrips()
    {
        // Replace the entire ContactMethods collection and confirm a fresh context
        // reads back exactly the new items, verifying dictionary key serialization.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            customer.ContactMethods =
            [
                new OwnedTypeFixture.ContactMethod { Id = 1, Type = "fax", Value = "555-0199" },
                new OwnedTypeFixture.ContactMethod { Id = 2, Type = "sms", Value = "555-0200" }
            ];
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            Assert.Equal(2, customer.ContactMethods.Count);
            Assert.Contains(customer.ContactMethods, cm => cm.Type == "fax" && cm.Value == "555-0199");
            Assert.Contains(customer.ContactMethods, cm => cm.Type == "sms" && cm.Value == "555-0200");
        }
    }

    [Fact]
    public async Task OwnsMany_ClearCollection_RoundTrips()
    {
        // Writing an empty collection must overwrite the previously stored array.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            customer.ContactMethods = [];
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            Assert.Empty(customer.ContactMethods);
        }
    }

    // -------------------------------------------------------------------------
    // Read path — untested scenarios
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OwnsOne_FilterBy_OwnedProperty_ReturnsMatchingCustomer()
    {
        await using var ctx = fixture.GetDbContext();
        var customers = await ctx.Customers
            .Where(c => c.Address.City == "Springfield")
            .ToListAsync();

        Assert.Single(customers);
        Assert.Equal("Alice", customers[0].Name);
    }

    [Fact]
    public async Task OwnsMany_ToListAsync_AllCustomersCollectionsPopulated()
    {
        await using var ctx = fixture.GetDbContext();
        var customers = await ctx.Customers.ToListAsync();

        Assert.Equal(2, customers.Count);
        var alice = customers.Single(c => c.Name == "Alice");
        var bob   = customers.Single(c => c.Name == "Bob");
        Assert.Equal(2, alice.ContactMethods.Count);
        Assert.Single(bob.ContactMethods);
    }

    [Fact]
    public async Task OwnsMany_AsNoTracking_IsPopulated()
    {
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers
            .AsNoTracking()
            .FirstAsync(c => c.CustomerId == 1);

        Assert.Equal(2, customer.ContactMethods.Count);
        Assert.Contains(customer.ContactMethods, cm => cm.Type == "email");
        Assert.Contains(customer.ContactMethods, cm => cm.Type == "phone");
    }

    [Fact]
    public async Task OwnsMany_ItemOrderIsPreserved()
    {
        // Seed stores email first, phone second for customer 1.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);

        Assert.Equal(2, customer.ContactMethods.Count);
        Assert.Equal("email", customer.ContactMethods[0].Type);
        Assert.Equal("phone", customer.ContactMethods[1].Type);
    }

    [Fact]
    public async Task OwnsMany_EmptyCollectionDocument_ReadsAsEmpty()
    {
        // A document stored with an empty contactMethods array must read back as
        // an empty list, not null.
        const int id = 97;
        await using (var ctx = fixture.GetDbContext())
        {
            ctx.Update(new OwnedTypeFixture.Customer
            {
                CustomerId = id,
                Name = "Empty",
                Address = new OwnedTypeFixture.Address { Street = "0 Void Ln", City = "Nowhere" },
                ContactMethods = []
            });
            await ctx.SaveChangesAsync();
        }

        try
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == id);
            Assert.NotNull(customer.ContactMethods);
            Assert.Empty(customer.ContactMethods);
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    // -------------------------------------------------------------------------
    // Write path — untested scenarios
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Customer_Add_WithOwnedTypes_RoundTrips()
    {
        const int id = 99;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Add(new OwnedTypeFixture.Customer
                {
                    CustomerId = id,
                    Name = "Charlie",
                    Address = new OwnedTypeFixture.Address { Street = "3 Oak Ave", City = "Shelbyville" },
                    ContactMethods =
                    [
                        new OwnedTypeFixture.ContactMethod { Id = 1, Type = "email", Value = "charlie@example.com" }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == id);
                Assert.Equal("Charlie", customer.Name);
                Assert.Equal("3 Oak Ave", customer.Address.Street);
                Assert.Equal("Shelbyville", customer.Address.City);
                Assert.Single(customer.ContactMethods);
                Assert.Equal("charlie@example.com", customer.ContactMethods[0].Value);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task Customer_Delete_RemovesDocument()
    {
        const int id = 98;
        await using (var ctx = fixture.GetDbContext())
        {
            ctx.Update(new OwnedTypeFixture.Customer
            {
                CustomerId = id,
                Name = "Transient",
                Address = new OwnedTypeFixture.Address { Street = "Temp St", City = "Nowhere" },
                ContactMethods = []
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == id);
            ctx.Remove(customer);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
            Assert.Null(customer);
        }
    }

    [Fact]
    public async Task OwnsMany_AddSingleItem_RoundTrips()
    {
        // Add one item to an existing collection via .Add() without replacing the list
        // reference; the owner document must be rewritten.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            customer.ContactMethods.Add(
                new OwnedTypeFixture.ContactMethod { Id = 2, Type = "phone", Value = "555-0202" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            Assert.Equal(2, customer.ContactMethods.Count);
            Assert.Contains(customer.ContactMethods, cm => cm.Type == "email");
            Assert.Contains(customer.ContactMethods, cm => cm.Type == "phone" && cm.Value == "555-0202");
        }
    }

    [Fact]
    public async Task OwnsMany_RemoveSingleItem_RoundTrips()
    {
        // Remove one item from a two-item collection via .Remove() without replacing the
        // list reference; the owner document must be rewritten with the item absent.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            var email = customer.ContactMethods.First(cm => cm.Type == "email");
            customer.ContactMethods.Remove(email);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            Assert.Single(customer.ContactMethods);
            Assert.DoesNotContain(customer.ContactMethods, cm => cm.Type == "email");
            Assert.Contains(customer.ContactMethods, cm => cm.Type == "phone");
        }
    }

    [Fact]
    public async Task OwnsMany_MutateItemProperty_RoundTrips()
    {
        // Mutate a scalar property on an existing owned item in-place; the owner
        // document must be rewritten with the updated value.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            customer.ContactMethods.First(cm => cm.Type == "email").Value = "alice.new@example.com";
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            Assert.Contains(customer.ContactMethods,
                cm => cm.Type == "email" && cm.Value == "alice.new@example.com");
        }
    }
}
