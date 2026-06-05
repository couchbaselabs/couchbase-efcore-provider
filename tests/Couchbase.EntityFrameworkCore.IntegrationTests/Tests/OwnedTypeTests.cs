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

        Assert.Equal(3, customers.Count);
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

    // -------------------------------------------------------------------------
    // Content-snapshot regression tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OwnsMany_MutateItemProperty_SaveTwice_SecondSaveIsNoOp()
    {
        // After a successful save, RefreshOwnedCollectionSnapshots must update
        // OriginalItems so that a second SaveChangesAsync on the same context
        // (with no further mutations) does not falsely detect a change and
        // trigger a redundant write.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);

        // First mutation and save
        customer.ContactMethods.First(cm => cm.Type == "email").Value = "alice.v2@example.com";
        var firstCount = await ctx.SaveChangesAsync();
        Assert.Equal(1, firstCount);   // one document written

        // No further mutations — second save must be a no-op
        var secondCount = await ctx.SaveChangesAsync();
        Assert.Equal(0, secondCount);  // nothing to write (snapshot was refreshed after save 1)
    }

    [Fact]
    public async Task OwnsMany_AddItem_SaveTwice_SecondSaveIsNoOp()
    {
        // Same regression check as above but for the .Add() / count-change path.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);

        customer.ContactMethods.Add(
            new OwnedTypeFixture.ContactMethod { Id = 2, Type = "fax", Value = "555-0300" });
        var firstCount = await ctx.SaveChangesAsync();
        Assert.Equal(1, firstCount);

        var secondCount = await ctx.SaveChangesAsync();
        Assert.Equal(0, secondCount);
    }

    // -------------------------------------------------------------------------
    // Nested owned-type materialisation (Phase 4.3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OwnsOne_Nested_InOwnsMany_IsPopulated()
    {
        // ContactMethod.Label is an OwnsOne nested inside an OwnsMany.
        // MaterializeOwnedItem must recurse into the nested object and set the property.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 3);

        Assert.Equal(2, customer.ContactMethods.Count);
        var email = customer.ContactMethods.First(cm => cm.Type == "email");
        Assert.NotNull(email.Label);
        Assert.Equal("Work Email", email.Label.DisplayName);
        var phone = customer.ContactMethods.First(cm => cm.Type == "phone");
        Assert.NotNull(phone.Label);
        Assert.Equal("Mobile", phone.Label.DisplayName);
    }

    [Fact]
    public async Task OwnsMany_Nested_InOwnsMany_IsPopulated()
    {
        // ContactMethod.Tags is an OwnsMany nested inside an OwnsMany.
        // MaterializeOwnedItem must recurse into the nested array and materialise each element.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 3);

        var email = customer.ContactMethods.First(cm => cm.Type == "email");
        Assert.Equal(2, email.Tags.Count);
        Assert.Contains(email.Tags, t => t.Key == "priority" && t.Val == "high");
        Assert.Contains(email.Tags, t => t.Key == "verified" && t.Val == "true");

        var phone = customer.ContactMethods.First(cm => cm.Type == "phone");
        Assert.Empty(phone.Tags);
    }

    [Fact]
    public async Task OwnsOne_WithExplicitInclude_IsPopulated()
    {
        // Explicit .Include(c => c.Address) on an OwnsOne must not break materialisation.
        // The EF Core relational shaper already projects OwnsOne columns, so Include is a no-op.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers
            .Include(c => c.Address)
            .FirstAsync(c => c.CustomerId == 1);

        Assert.NotNull(customer.Address);
        Assert.Equal("1 Main St", customer.Address.Street);
        Assert.Equal("Springfield", customer.Address.City);
    }

    [Fact]
    public async Task OwnsMany_NestedOwned_ExistingCustomers_HaveNullOrEmpty()
    {
        // Customers 1 and 2 were seeded without Label/Tags on their ContactMethods.
        // Nested owned navigations must default to null / empty — not throw.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 1);

        Assert.All(customer.ContactMethods, cm =>
        {
            Assert.Null(cm.Label);
            Assert.Empty(cm.Tags);
        });
    }

    [Fact]
    public async Task OwnsMany_AddAndMutate_BothChangesSaved()
    {
        // Add one new item and mutate an existing item's property in the same
        // SaveChangesAsync call — both changes must appear in the persisted document.
        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            // Mutate the existing item
            customer.ContactMethods.First(cm => cm.Type == "email").Value = "bob.updated@example.com";
            // Add a second item
            customer.ContactMethods.Add(
                new OwnedTypeFixture.ContactMethod { Id = 2, Type = "sms", Value = "555-0300" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.GetDbContext())
        {
            var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 2);
            Assert.Equal(2, customer.ContactMethods.Count);
            Assert.Contains(customer.ContactMethods,
                cm => cm.Type == "email" && cm.Value == "bob.updated@example.com");
            Assert.Contains(customer.ContactMethods,
                cm => cm.Type == "sms"   && cm.Value == "555-0300");
        }
    }

    // -------------------------------------------------------------------------
    // HashSet<T> collection type — ICollection<T> fallback clear path
    // -------------------------------------------------------------------------
    // These tests verify that MaterializeOwnedItem correctly clears and repopulates
    // a HashSet<T>-backed OwnsMany navigation.  Before the fix, the non-IList branch
    // silently skipped the clear, which could leave pre-populated items in the set.

    [Fact]
    public async Task OwnsMany_HashSet_IsPopulated_OnFirstRead()
    {
        // Baseline: a HashSet<T> navigation is populated correctly from JSON.
        const int id = 200;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.HashSetCustomer
                {
                    Id = id,
                    Name = "HashSet Alice",
                    Tags =
                    [
                        new OwnedTypeFixture.HashSetTag { Id = 1, Value = "tag-a" },
                        new OwnedTypeFixture.HashSetTag { Id = 2, Value = "tag-b" }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.HashSetCustomers.FirstAsync(c => c.Id == id);
                Assert.Equal(2, customer.Tags.Count);
                Assert.Contains(customer.Tags, t => t.Value == "tag-a");
                Assert.Contains(customer.Tags, t => t.Value == "tag-b");
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.HashSetCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task OwnsMany_HashSet_NoDuplicates_WhenQueriedMultipleTimes()
    {
        // Regression guard: querying the same HashSet<T>-backed entity multiple times
        // must not accumulate duplicate items.  Before the fix, the non-IList clear
        // was a no-op so each rematerialization appended to the existing set.
        const int id = 201;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.HashSetCustomer
                {
                    Id = id,
                    Name = "HashSet Bob",
                    Tags =
                    [
                        new OwnedTypeFixture.HashSetTag { Id = 1, Value = "only-tag" }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            // Query twice in the same logical scope (fresh context each time to
            // rule out EF identity-cache short-circuits).
            for (var pass = 1; pass <= 2; pass++)
            {
                await using var ctx = fixture.GetDbContext();
                var customer = await ctx.HashSetCustomers.FirstAsync(c => c.Id == id);
                Assert.Single(customer.Tags);
                Assert.Equal("only-tag", customer.Tags.Single().Value);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.HashSetCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task OwnsMany_HashSet_Update_RoundTrips()
    {
        // Mutating a HashSet<T>-backed OwnsMany must persist and read back correctly.
        const int id = 202;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.HashSetCustomer
                {
                    Id = id,
                    Name = "HashSet Carol",
                    Tags =
                    [
                        new OwnedTypeFixture.HashSetTag { Id = 1, Value = "original" }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.HashSetCustomers.FirstAsync(c => c.Id == id);
                customer.Tags = [new OwnedTypeFixture.HashSetTag { Id = 1, Value = "updated" }];
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.HashSetCustomers.FirstAsync(c => c.Id == id);
                Assert.Single(customer.Tags);
                Assert.Equal("updated", customer.Tags.Single().Value);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.HashSetCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task OwnsMany_HashSet_EmptyCollection_ReadsAsEmpty()
    {
        // A HashSet<T> navigation stored with zero items must read back as empty, not null.
        const int id = 203;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.HashSetCustomer
                {
                    Id = id,
                    Name = "HashSet Dave",
                    Tags = []
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.HashSetCustomers.FirstAsync(c => c.Id == id);
                Assert.NotNull(customer.Tags);
                Assert.Empty(customer.Tags);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.HashSetCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    // -------------------------------------------------------------------------
    // Depth-3 OwnsMany (Customer → ContactMethod → ContactTag → TagAudit)
    // These tests verify that IsAllOwnedTablesSelect recurses correctly so that
    // the lateral-join subquery for TagAudit is suppressed (no empty FROM) and
    // its ORDER BY alias is dropped (no dangling alias).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OwnsMany_Depth3_Audits_ArePopulated()
    {
        // Carol's first tag (priority=high) has two audits; the second has none.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 3);

        var email = customer.ContactMethods.First(cm => cm.Type == "email");
        var priority = email.Tags.First(t => t.Key == "priority");

        Assert.Equal(2, priority.Audits.Count);
        Assert.Contains(priority.Audits, a => a.Note == "set by admin");
        Assert.Contains(priority.Audits, a => a.Note == "confirmed");
    }

    [Fact]
    public async Task OwnsMany_Depth3_EmptyAudits_ReadsAsEmpty()
    {
        // The "verified" tag has an empty Audits list — must read back as empty, not null.
        await using var ctx = fixture.GetDbContext();
        var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == 3);

        var email = customer.ContactMethods.First(cm => cm.Type == "email");
        var verified = email.Tags.First(t => t.Key == "verified");

        Assert.NotNull(verified.Audits);
        Assert.Empty(verified.Audits);
    }

    [Fact]
    public async Task OwnsMany_Depth3_ToList_DoesNotThrow()
    {
        // ToListAsync on a depth-3 model previously caused N1QL error 3000
        // (empty FROM clause) due to the non-recursive IsAllOwnedTablesSelect.
        await using var ctx = fixture.GetDbContext();
        var customers = await ctx.Customers.ToListAsync();
        Assert.NotEmpty(customers);
    }

    [Fact]
    public async Task OwnsMany_Depth3_Audits_RoundTrip()
    {
        // Write a new customer with depth-3 data and confirm it reads back correctly.
        const int id = 300;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.Customer
                {
                    CustomerId = id,
                    Name = "Depth3 Test",
                    Address = new OwnedTypeFixture.Address { Street = "1 Deep St", City = "Recursion" },
                    ContactMethods =
                    [
                        new OwnedTypeFixture.ContactMethod
                        {
                            Id = 1, Type = "email", Value = "deep@test.com",
                            Tags =
                            [
                                new OwnedTypeFixture.ContactTag
                                {
                                    Id = 1, Key = "level", Val = "3",
                                    Audits =
                                    [
                                        new OwnedTypeFixture.TagAudit { Id = 1, Note = "depth-3 note" }
                                    ]
                                }
                            ]
                        }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.Customers.FirstAsync(c => c.CustomerId == id);
                var tag = customer.ContactMethods.Single().Tags.Single();
                Assert.Single(tag.Audits);
                Assert.Equal("depth-3 note", tag.Audits[0].Note);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var customer = await ctx.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer != null) { ctx.Remove(customer); await ctx.SaveChangesAsync(); }
        }
    }

    // -------------------------------------------------------------------------
    // Field-access / get-only owned properties
    // Verifies that FieldInfo fallback in MaterializeOwnedItem and SerializeOwnedItem
    // correctly reads and writes properties that have no setter.
    // Also exercises the ICollection<T> non-IList nested clear path via
    // FieldContact.Tags (HashSet<FieldTag>).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FieldAccess_OwnsOne_GetOnlyScalars_RoundTrip()
    {
        // A FieldAddress with get-only Street/City must survive a write → read cycle.
        const int id = 400;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.FieldAccessCustomer
                {
                    Id = id,
                    Name = "FieldAccess Alice",
                    Address = new OwnedTypeFixture.FieldAddress("10 Elm St", "Greenfield")
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.FieldAccessCustomers.FirstAsync(c => c.Id == id);
                Assert.NotNull(customer.Address);
                Assert.Equal("10 Elm St",  customer.Address.Street);
                Assert.Equal("Greenfield", customer.Address.City);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var c = await ctx.FieldAccessCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (c != null) { ctx.Remove(c); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task FieldAccess_OwnsMany_GetOnlyScalar_RoundTrip()
    {
        // FieldContact.Label has no setter — must be written and read back via FieldInfo.
        const int id = 401;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.FieldAccessCustomer
                {
                    Id = id,
                    Name = "FieldAccess Bob",
                    Address = new OwnedTypeFixture.FieldAddress("2 Oak Ave", "Lakewood"),
                    Contacts =
                    [
                        new OwnedTypeFixture.FieldContact(1, "work"),
                        new OwnedTypeFixture.FieldContact(2, "home")
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.FieldAccessCustomers.FirstAsync(c => c.Id == id);
                Assert.Equal(2, customer.Contacts.Count);
                Assert.Contains(customer.Contacts, c => c.Label == "work");
                Assert.Contains(customer.Contacts, c => c.Label == "home");
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var c = await ctx.FieldAccessCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (c != null) { ctx.Remove(c); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task FieldAccess_NestedHashSet_RoundTrip()
    {
        // FieldContact.Tags is a HashSet<FieldTag> nested inside an OwnsMany item.
        // Exercises the ICollection<T> non-IList clear path inside MaterializeOwnedItem.
        const int id = 402;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.FieldAccessCustomer
                {
                    Id = id,
                    Name = "FieldAccess Carol",
                    Address = new OwnedTypeFixture.FieldAddress("3 Pine Rd", "Riverdale"),
                    Contacts =
                    [
                        new OwnedTypeFixture.FieldContact(1, "primary")
                        {
                            Tags =
                            [
                                new OwnedTypeFixture.FieldTag { Id = 1, Key = "vip" },
                                new OwnedTypeFixture.FieldTag { Id = 2, Key = "active" }
                            ]
                        }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.FieldAccessCustomers.FirstAsync(c => c.Id == id);
                var contact = customer.Contacts.Single();
                Assert.Equal("primary", contact.Label);
                Assert.Equal(2, contact.Tags.Count);
                Assert.Contains(contact.Tags, t => t.Key == "vip");
                Assert.Contains(contact.Tags, t => t.Key == "active");
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var c = await ctx.FieldAccessCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (c != null) { ctx.Remove(c); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task FieldAccess_NestedHashSet_NoDuplicatesOnRequery()
    {
        // Querying the same HashSet<FieldTag>-backed contact twice (fresh context each
        // time) must not accumulate duplicates — the ICollection<T> clear must fire.
        const int id = 403;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.FieldAccessCustomer
                {
                    Id = id,
                    Name = "FieldAccess Dave",
                    Address = new OwnedTypeFixture.FieldAddress("4 Maple Ln", "Springdale"),
                    Contacts =
                    [
                        new OwnedTypeFixture.FieldContact(1, "only")
                        {
                            Tags = [new OwnedTypeFixture.FieldTag { Id = 1, Key = "solo" }]
                        }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            for (var pass = 1; pass <= 2; pass++)
            {
                await using var ctx = fixture.GetDbContext();
                var customer = await ctx.FieldAccessCustomers.FirstAsync(c => c.Id == id);
                Assert.Single(customer.Contacts.Single().Tags);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var c = await ctx.FieldAccessCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (c != null) { ctx.Remove(c); await ctx.SaveChangesAsync(); }
        }
    }

    // -------------------------------------------------------------------------
    // HasConversion on OwnsMany item scalars (CQE Phase 3)
    // Verifies that ConvertFromJson (read) and SerializeOwnedItem (write) both
    // apply the value converter so the stored representation is the provider type
    // (string) and the materialised value is the model CLR type (ContactStatus).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HasConversion_OwnsMany_Scalar_RoundTrips()
    {
        // ContactStatus is stored as a string but materialised as the enum.
        const int id = 500;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.ConvertedCustomer
                {
                    Id   = id,
                    Name = "Converted Alice",
                    Contacts =
                    [
                        new OwnedTypeFixture.ConvertedContact { Id = 1, Label = "work",  Status = OwnedTypeFixture.ContactStatus.Active   },
                        new OwnedTypeFixture.ConvertedContact { Id = 2, Label = "home",  Status = OwnedTypeFixture.ContactStatus.Inactive  },
                        new OwnedTypeFixture.ConvertedContact { Id = 3, Label = "other", Status = OwnedTypeFixture.ContactStatus.Pending   }
                    ]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.ConvertedCustomers.FirstAsync(c => c.Id == id);
                Assert.Equal(3, customer.Contacts.Count);
                Assert.Equal(OwnedTypeFixture.ContactStatus.Active,   customer.Contacts.First(c => c.Label == "work").Status);
                Assert.Equal(OwnedTypeFixture.ContactStatus.Inactive, customer.Contacts.First(c => c.Label == "home").Status);
                Assert.Equal(OwnedTypeFixture.ContactStatus.Pending,  customer.Contacts.First(c => c.Label == "other").Status);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var c = await ctx.ConvertedCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (c != null) { ctx.Remove(c); await ctx.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task HasConversion_OwnsMany_Scalar_Update_RoundTrips()
    {
        // Mutate the converted scalar and confirm the new value round-trips.
        const int id = 501;
        try
        {
            await using (var ctx = fixture.GetDbContext())
            {
                ctx.Update(new OwnedTypeFixture.ConvertedCustomer
                {
                    Id      = id,
                    Name    = "Converted Bob",
                    Contacts = [new OwnedTypeFixture.ConvertedContact { Id = 1, Label = "main", Status = OwnedTypeFixture.ContactStatus.Active }]
                });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.ConvertedCustomers.FirstAsync(c => c.Id == id);
                customer.Contacts[0].Status = OwnedTypeFixture.ContactStatus.Inactive;
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = fixture.GetDbContext())
            {
                var customer = await ctx.ConvertedCustomers.FirstAsync(c => c.Id == id);
                Assert.Equal(OwnedTypeFixture.ContactStatus.Inactive, customer.Contacts[0].Status);
            }
        }
        finally
        {
            await using var ctx = fixture.GetDbContext();
            var c = await ctx.ConvertedCustomers.FirstOrDefaultAsync(c => c.Id == id);
            if (c != null) { ctx.Remove(c); await ctx.SaveChangesAsync(); }
        }
    }
}
