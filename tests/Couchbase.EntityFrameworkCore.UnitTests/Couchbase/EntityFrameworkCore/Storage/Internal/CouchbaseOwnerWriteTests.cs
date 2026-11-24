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
/// already written. For an owned type declared on a base type, that check used the base type while
/// the owner was recorded under its derived type, so a derived owner was written twice: an insert
/// and an upsert sent at the same time, and the insert failed whenever the upsert landed first.
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
        Assert.Equal("2", write.Key);
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

    private class AnimalContext : DbContext
    {
        public DbSet<Animal> Animals { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseCouchbaseProvider(
                new ClusterOptions()
                    .WithConnectionString("couchbase://localhost")
                    .WithPasswordAuthentication("Administrator", "password"),
                _ => { });

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
}
