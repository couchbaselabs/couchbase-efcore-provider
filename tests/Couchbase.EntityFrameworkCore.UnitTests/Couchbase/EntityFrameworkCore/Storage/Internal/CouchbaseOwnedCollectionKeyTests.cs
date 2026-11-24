using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// An <c>OwnsMany</c> item type with no key of its own is keyed by its owner's key plus an integer
/// EF Core generates (<c>Id</c>, or <c>__synthesizedOrdinal</c> with <c>ToJson()</c>). The save must
/// give that integer a real value: EF Core drops temporary values when it accepts the changes, so
/// without one every item gets 0 and change detection throws "already being tracked".
/// </summary>
/// <remarks>
/// Each test does what <c>SaveChangesAsync</c> does around the write, without a cluster: it takes the
/// entries to save, assigns the keys, accepts the changes and runs change detection.
/// </remarks>
public class CouchbaseOwnedCollectionKeyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddedItems_GetTheirPositions_AndSurviveAcceptingTheChanges(bool json)
    {
        using var ctx = OrderContext.Create(json);
        var order = new Order { Id = 1, Lines = [new() { Sku = "a" }, new() { Sku = "b" }, new() { Sku = "c" }] };
        ctx.Orders.Add(order);

        Save(ctx);

        Assert.Equal([1L, 2L, 3L], KeyValues(ctx, order.Lines));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ItemAddedAfterASave_GetsTheNextValue_AndKeepsTheOthers(bool json)
    {
        using var ctx = OrderContext.Create(json);
        var order = new Order { Id = 1, Lines = [new() { Sku = "a" }, new() { Sku = "b" }] };
        ctx.Orders.Add(order);
        Save(ctx);

        order.Lines.Insert(0, new Line { Sku = "new" });
        Save(ctx);

        Assert.Equal([3L, 1L, 2L], KeyValues(ctx, order.Lines));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachOwner_NumbersItsOwnItems(bool json)
    {
        using var ctx = OrderContext.Create(json);
        var first = new Order { Id = 1, Lines = [new() { Sku = "a" }, new() { Sku = "b" }] };
        var second = new Order { Id = 2, Lines = [new() { Sku = "c" }, new() { Sku = "d" }] };
        ctx.Orders.AddRange(first, second);

        Save(ctx);

        Assert.Equal([1L, 2L], KeyValues(ctx, first.Lines));
        Assert.Equal([1L, 2L], KeyValues(ctx, second.Lines));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedCollections_AreNumberedWithinEachItem(bool json)
    {
        using var ctx = OrderContext.Create(json);
        var order = new Order
        {
            Id = 1,
            Lines =
            [
                new() { Sku = "a", Notes = [new() { Text = "x" }, new() { Text = "y" }] },
                new() { Sku = "b", Notes = [new() { Text = "z" }, new() { Text = "w" }] },
            ],
        };
        ctx.Orders.Add(order);

        Save(ctx);

        Assert.Equal([1L, 2L], KeyValues(ctx, order.Lines));
        Assert.Equal([1L, 2L], KeyValues(ctx, order.Lines[0].Notes));
        Assert.Equal([1L, 2L], KeyValues(ctx, order.Lines[1].Notes));
    }

    [Fact]
    public void AKeyTheApplicationSet_IsKept()
    {
        using var ctx = OrderContext.Create(json: false);
        var order = new Order { Id = 1, Tags = [new() { Id = 7, Name = "a" }, new() { Id = 9, Name = "b" }] };
        ctx.Orders.Add(order);

        Save(ctx);

        Assert.Equal([7L, 9L], KeyValues(ctx, order.Tags));
    }

    // What SaveChangesAsync does around the write: detect changes, assign the keys, then accept the
    // changes. Change detection after that is what threw before the keys were assigned.
    private static void Save(DbContext ctx)
    {
        ctx.ChangeTracker.DetectChanges();
        var stateManager = ctx.GetService<IStateManager>();
        CouchbaseDatabaseWrapper.AssignOwnedCollectionKeys(stateManager.GetEntriesToSave(cascadeChanges: true));
        stateManager.AcceptAllChanges();
        ctx.ChangeTracker.DetectChanges();
    }

    // The generated key value of each item, in collection order.
    private static long[] KeyValues<T>(DbContext ctx, IEnumerable<T> items) where T : class
        => items.Select(item =>
            {
                var entry = ctx.Entry(item);
                var keyProperty = entry.Metadata.FindPrimaryKey()!.Properties[^1];
                Assert.False(entry.Property(keyProperty.Name).IsTemporary);
                return Convert.ToInt64(entry.Property(keyProperty.Name).CurrentValue);
            })
            .ToArray();

    private class Order
    {
        public int Id { get; set; }
        public List<Line> Lines { get; set; } = [];
        public List<Tag> Tags { get; set; } = [];
    }

    private class Line
    {
        public string Sku { get; set; } = string.Empty;
        public List<Note> Notes { get; set; } = [];
    }

    private class Note
    {
        public string Text { get; set; } = string.Empty;
    }

    private class Tag
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // EF Core caches one model per context type, so each mapping has its own type.
    private abstract class OrderContext(bool json) : DbContext
    {
        public static OrderContext Create(bool json) => json ? new JsonOrderContext() : new PlainOrderContext();

        public DbSet<Order> Orders { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseCouchbaseProvider(
                new ClusterOptions()
                    .WithConnectionString("couchbase://localhost")
                    .WithPasswordAuthentication("Administrator", "password"),
                _ => { });

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.Property(o => o.Id).ValueGeneratedNever();
                b.OwnsMany(o => o.Lines, l =>
                {
                    l.OwnsMany(x => x.Notes);
                    if (json)
                    {
                        l.ToJson();
                    }
                });
                b.OwnsMany(o => o.Tags);
            });
    }

    private sealed class PlainOrderContext() : OrderContext(json: false);

    private sealed class JsonOrderContext() : OrderContext(json: true);
}
