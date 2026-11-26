using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

// The test replaces ICouchbaseClientWrapper, an internal provider service.
#pragma warning disable EF1001

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Saves owned collections through <c>SaveChangesAsync</c>, with a client that records the documents
/// instead of writing them, and checks the item keys in the document and in the change tracker.
/// </summary>
public class CouchbaseOwnedCollectionSaveTests
{
    [Fact]
    public async Task KeylessItems_AreWrittenAndCanBeAddedToAfterTheSave()
    {
        await using var ctx = new OrderContext();
        var order = new Order { Id = 1, Lines = [new() { Sku = "a" }, new() { Sku = "b" }] };
        ctx.Orders.Add(order);

        await ctx.SaveChangesAsync();

        var insert = Assert.Single(ctx.Client.Writes);
        Assert.Equal("Create", insert.Kind);
        Assert.Equal(["a", "b"], Items(insert, "Lines").Select(i => Field(i, "Sku").GetString()));

        // The owner was added in this context: a later change to its collection is saved too.
        order.Lines.Add(new Line { Sku = "c" });
        await ctx.SaveChangesAsync();

        var update = ctx.Client.Writes[1];
        Assert.Equal("Update", update.Kind);
        Assert.Equal(["a", "b", "c"], Items(update, "Lines").Select(i => Field(i, "Sku").GetString()));
    }

    [Fact]
    public async Task ItemsWithAClrKeyLeftAtZero_AreWrittenWithTheGeneratedValues()
    {
        await using var ctx = new OrderContext();
        var order = new Order { Id = 1, Tags = [new() { Name = "a" }, new() { Name = "b" }] };
        ctx.Orders.Add(order);

        await ctx.SaveChangesAsync();

        var insert = Assert.Single(ctx.Client.Writes);
        Assert.Equal([1, 2], Items(insert, "Tags").Select(i => Field(i, "Id").GetInt32()));
        Assert.Equal([1, 2], order.Tags.Select(t => t.Id));
        Assert.Equal([1, 2], order.Tags.Select(t => (int)ctx.Entry(t).Property(nameof(Tag.Id)).CurrentValue!));
    }

    [Fact]
    public async Task ARetryAfterAFailedSave_WritesTheSameValues()
    {
        await using var ctx = new OrderContext();
        var order = new Order
        {
            Id = 1,
            Lines = [new() { Sku = "a" }, new() { Sku = "b" }],
            Tags = [new() { Name = "a" }, new() { Name = "b" }],
        };
        ctx.Orders.Add(order);
        ctx.Client.FailNextWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        await ctx.SaveChangesAsync();

        var insert = Assert.Single(ctx.Client.Writes);
        Assert.Equal([1, 2], Items(insert, "Tags").Select(i => Field(i, "Id").GetInt32()));
        Assert.Equal([1, 2], order.Tags.Select(t => t.Id));
        Assert.Equal([1, 2], order.Lines.Select(l => (int)ctx.Entry(l).Property("Id").CurrentValue!));
    }

    // The items of an owned collection in a written document.
    private static JsonElement[] Items(Write write, string navigation)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(write.Document));
        return Field(json.RootElement, navigation).EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    // A document field, whatever naming policy the context uses.
    private static JsonElement Field(JsonElement element, string name)
        => element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private sealed record Write(string Kind, string Id, object? Document);

    // Records each write; scoped like the client it replaces, so the context resolves this instance.
    private sealed class RecordingClient : ICouchbaseClientWrapper
    {
        public List<Write> Writes { get; } = [];

        public string BucketName => "bucket";

        public Task<IMutationResult> CreateDocument<TEntity>(string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
            => Record("Create", id, entity);

        public Task<IMutationResult> UpdateDocument<TEntity>(string id, string keyspace, TEntity entity, ulong? cas = null, CancellationToken cancellationToken = default)
            => Record("Update", id, entity);

        public Task<bool> DeleteDocument(string id, string keyspace, ulong? cas = null, CancellationToken cancellationToken = default)
        {
            lock (Writes) Writes.Add(new Write("Delete", id, null));
            return Task.FromResult(true);
        }

        public bool FailNextWrite { get; set; }

        private Task<IMutationResult> Record(string kind, string id, object? document)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("The write failed.");
            }

            lock (Writes) Writes.Add(new Write(kind, id, document));
            var result = new Mock<IMutationResult>();
            result.SetupGet(r => r.Cas).Returns(1UL);
            return Task.FromResult(result.Object);
        }

        public Task<ICouchbaseCollection> GetCollectionAsync(string keyspace, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueTransactionalInsert<TEntity>(CouchbaseDbTransaction transaction, string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueTransactionalUpsert<TEntity>(CouchbaseDbTransaction transaction, string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnqueueTransactionalRemove(CouchbaseDbTransaction transaction, string id, string keyspace, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private class Order
    {
        public int Id { get; set; }
        public List<Line> Lines { get; set; } = [];
        public List<Tag> Tags { get; set; } = [];
    }

    private class Line
    {
        public string Sku { get; set; } = string.Empty;
    }

    private class Tag
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class OrderContext : DbContext
    {
        public DbSet<Order> Orders { get; set; } = null!;

        public RecordingClient Client => (RecordingClient)this.GetService<ICouchbaseClientWrapper>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseCouchbaseProvider(
                    new ClusterOptions()
                        .WithConnectionString("couchbase://localhost")
                        .WithPasswordAuthentication("Administrator", "password"),
                    _ => { })
                .ReplaceService<ICouchbaseClientWrapper, RecordingClient>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.Property(o => o.Id).ValueGeneratedNever();
                b.OwnsMany(o => o.Lines);
                b.OwnsMany(o => o.Tags);
            });
    }
}
