using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// When an owned entity changes, the save also writes its owner's document, unless the owner is
/// already written.
/// </summary>
/// <remarks>
/// These tests plan the writes of a save without sending them, so no cluster is needed.
/// </remarks>
public class CouchbaseOwnerWriteTests
{
    [Fact]
    public void AddedDerivedEntity_WithOwnedTypeOnTheBaseType_IsWrittenOnce()
    {
        using var ctx = new AnimalContext();
        ctx.Animals.Add(new Dog { Id = 2, Tag = new Tag { Text = "base" }, Collar = new Tag { Text = "derived" } });

        var writes = PlanWrites(ctx);

        var write = Assert.Single(writes);
        Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Insert, write.Kind);
        Assert.Equal("2", write.Key);
    }

    [Fact]
    public void AddedBaseAndDerivedEntities_AreEachWrittenOnce()
    {
        using var ctx = new AnimalContext();
        ctx.Animals.AddRange(
            new Animal { Id = 1, Tag = new Tag { Text = "a" } },
            new Dog { Id = 2, Tag = new Tag { Text = "b" }, Collar = new Tag { Text = "c" } });

        var writes = PlanWrites(ctx);

        Assert.Equal(["1", "2"], writes.Select(w => w.Key).Order());
        Assert.All(writes, w => Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Insert, w.Kind));
    }

    [Fact]
    public void ModifiedDerivedEntity_WithChangedOwnedTypeOnTheBaseType_IsWrittenOnce()
    {
        using var ctx = new AnimalContext();
        var dog = new Dog { Id = 2, Name = "Rex", Tag = new Tag { Text = "old" }, Collar = new Tag { Text = "c" } };
        ctx.Attach(dog);
        dog.Name = "Max";
        dog.Tag.Text = "new";

        var writes = PlanWrites(ctx);

        var write = Assert.Single(writes);
        Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Upsert, write.Kind);
    }

    [Fact]
    public void UnchangedDerivedEntity_WithChangedOwnedTypeOnTheBaseType_WritesTheOwnerOnce()
    {
        // Only the owned entity changed, so the owner is written by the owner pass alone.
        using var ctx = new AnimalContext();
        var dog = new Dog { Id = 2, Tag = new Tag { Text = "old" }, Collar = new Tag { Text = "c" } };
        ctx.Attach(dog);
        dog.Tag.Text = "new";

        var writes = PlanWrites(ctx);

        var write = Assert.Single(writes);
        Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Upsert, write.Kind);
        Assert.Equal("2", write.Key);
    }

    [Fact]
    public void UnchangedOwner_WithTwoChangedOwnedTypes_IsWrittenOnce()
    {
        using var ctx = new AnimalContext();
        var dog = new Dog { Id = 2, Tag = new Tag { Text = "old" }, Collar = new Tag { Text = "old" } };
        ctx.Attach(dog);
        dog.Tag.Text = "new";
        dog.Collar.Text = "new";

        var writes = PlanWrites(ctx);

        var write = Assert.Single(writes);
        Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Upsert, write.Kind);
        Assert.Equal("2", write.Key);
    }

    [Fact]
    public void DeletedDerivedEntity_WithOwnedTypeOnTheBaseType_IsOnlyDeleted()
    {
        // Deleting the owner also deletes its owned entities; the owner must not also be upserted.
        using var ctx = new AnimalContext();
        var dog = new Dog { Id = 2, Tag = new Tag { Text = "a" }, Collar = new Tag { Text = "c" } };
        ctx.Attach(dog);
        ctx.Remove(dog);

        var writes = PlanWrites(ctx);

        var write = Assert.Single(writes);
        Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Delete, write.Kind);
        Assert.Equal("2", write.Key);
    }

    [Fact]
    public void UnchangedOwnersWithTheSameClrNameAndKey_WithChangedOwnedTypes_AreEachWritten()
    {
        using var ctx = new SameNameContext();
        var first = new First.Item { Id = 1, Detail = new First.Detail { Text = "old" } };
        var second = new Second.Item { Id = 1, Detail = new Second.Detail { Text = "old" } };
        ctx.AttachRange(first, second);
        first.Detail.Text = "new";
        second.Detail.Text = "new";

        var writes = PlanWrites(ctx);

        Assert.Equal(["bucket.scope.first", "bucket.scope.second"], writes.Select(w => w.Keyspace).Order());
        Assert.All(writes, w => Assert.Equal(CouchbaseDatabaseWrapper.CouchbaseWriteKind.Upsert, w.Kind));
    }

    private static IReadOnlyList<CouchbaseDatabaseWrapper.PendingWrite> PlanWrites(DbContext ctx)
    {
        ctx.ChangeTracker.DetectChanges();
        var entries = ctx.GetService<IStateManager>().GetEntriesToSave(cascadeChanges: true);
        return ((CouchbaseDatabaseWrapper)ctx.GetService<IDatabase>()).PlanWrites(entries);
    }

    private class Animal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Tag Tag { get; set; } = null!;
    }

    private class Dog : Animal
    {
        public Tag Collar { get; set; } = null!;
    }

    private class Tag
    {
        public string Text { get; set; } = string.Empty;
    }

    private abstract class TestContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseCouchbaseProvider(
                new ClusterOptions()
                    .WithConnectionString("couchbase://localhost")
                    .WithPasswordAuthentication("Administrator", "password"),
                _ => { });
    }

    private class AnimalContext : TestContext
    {
        public DbSet<Animal> Animals { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "animals");
                b.Property(a => a.Id).ValueGeneratedNever();
                b.OwnsOne(a => a.Tag);
            });
            modelBuilder.Entity<Dog>(b =>
            {
                b.HasBaseType<Animal>();
                b.OwnsOne(d => d.Collar);
            });
        }
    }

    // Two entity types with the same short CLR name, in different containing types.
    private static class First
    {
        public class Item
        {
            public int Id { get; set; }
            public Detail Detail { get; set; } = null!;
        }

        public class Detail
        {
            public string Text { get; set; } = string.Empty;
        }
    }

    private static class Second
    {
        public class Item
        {
            public int Id { get; set; }
            public Detail Detail { get; set; } = null!;
        }

        public class Detail
        {
            public string Text { get; set; } = string.Empty;
        }
    }

    private class SameNameContext : TestContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<First.Item>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "first");
                b.Property(i => i.Id).ValueGeneratedNever();
                b.OwnsOne(i => i.Detail);
            });
            modelBuilder.Entity<Second.Item>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "second");
                b.Property(i => i.Id).ValueGeneratedNever();
                b.OwnsOne(i => i.Detail);
            });
        }
    }
}
