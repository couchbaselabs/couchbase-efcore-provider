using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// An <c>OwnsMany</c> collection whose item type has no key of its own: EF Core keys each item by
/// its owner's key plus an integer it generates. Saving two or more items used to throw "already
/// being tracked" after the document was written, because the generated value was never set.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class OwnedCollectionKeyTests(BloggingFixture fixture)
{
    // Plain OwnsMany only: the query path does not support ToJson() collections (the unit tests
    // cover the ToJson() key, which EF Core names __synthesizedOrdinal).
    [Fact]
    public async Task KeylessItems_SaveReadAndAddAgain()
    {
        var collectionName = "ownedkeys" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var ctx = CreateContext(collectionName))
            {
                await ctx.Database.EnsureCreatedAsync();
                var order = new Order { Id = 1, Lines = [new() { Sku = "a", Qty = 1 }, new() { Sku = "b", Qty = 2 }] };
                ctx.Orders.Add(order);
                await ctx.SaveChangesAsync();

                // A second save in the same context: change an item and add one.
                order.Lines[0].Qty = 10;
                order.Lines.Add(new Line { Sku = "c", Qty = 3 });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(collectionName))
            {
                var order = await ctx.Orders.SingleAsync(o => o.Id == 1);
                Assert.Equal(["a", "b", "c"], order.Lines.Select(l => l.Sku));
                Assert.Equal([10, 2, 3], order.Lines.Select(l => l.Qty));

                // Items read back are tracked again; adding to them must save too.
                order.Lines.Add(new Line { Sku = "d", Qty = 4 });
                await ctx.SaveChangesAsync();
            }

            await using (var ctx = CreateContext(collectionName))
            {
                var order = await ctx.Orders.SingleAsync(o => o.Id == 1);
                Assert.Equal(["a", "b", "c", "d"], order.Lines.Select(l => l.Sku));
            }
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private async Task DropCollectionAsync(string collectionName)
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    private OrderContext CreateContext(string collectionName)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrderContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.AutoCreateIndexes = true;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return new OrderContext(optionsBuilder.Options, collectionName);
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Line> Lines { get; set; } = [];
    }

    public class Line
    {
        public string Sku { get; set; } = string.Empty;
        public int Qty { get; set; }
    }

    public class OrderContext(DbContextOptions options, string collectionName) : DbContext(options)
    {
        public DbSet<Order> Orders { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection(this, collectionName);
                b.Property(o => o.Id).ValueGeneratedNever();
                b.OwnsMany(o => o.Lines);
            });
    }
}
