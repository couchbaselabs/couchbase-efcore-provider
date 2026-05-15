using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class OwnedTypeTests(
    OwnedTypeFixture fixture,
    ITestOutputHelper output)
{
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
        Assert.All(customers, c => Assert.NotEmpty(c.Address.Street));
    }

    [Fact]
    public async Task OwnsOne_Update_RoundTrips()
    {
        // Write a new address via SaveChangesAsync and confirm a fresh context reads it back.
        // The owner must be marked Modified explicitly: EF Core tracks the OwnsOne entity
        // in its own entry and our provider skips owned entries in the SaveChanges loop.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            customer.Address.Street = "99 Updated Ln";
            customer.Address.City = "Shelbyville";
            ctx.Entry(customer).State = EntityState.Modified;
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
            ctx.Entry(customer).State = EntityState.Modified;
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
            ctx.Entry(customer).State = EntityState.Modified;
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
            ctx.Entry(customer).State = EntityState.Modified;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);
            Assert.Empty(customer.ContactMethods);
        }
    }
}
