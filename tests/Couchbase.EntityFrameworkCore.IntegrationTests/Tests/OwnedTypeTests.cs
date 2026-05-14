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
}
