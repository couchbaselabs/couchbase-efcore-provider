using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The provider writes one document per entity, keyed only by the primary key, in the collection of
/// the entity's own type. Two mappings do not fit that, so the model is rejected when it is built:
/// table splitting (two entity types in one collection would write to the same document) and TPT
/// (queries join a base collection that the save path never writes to). Types that legitimately
/// share a collection (a TPH hierarchy, owned types) and TPC hierarchies are still accepted.
/// </summary>
public class CouchbaseMappingValidationTests
{
    [Fact]
    public void TableSplitting_PrimaryKeyToPrimaryKeyOneToOne_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.HasOne(o => o.Details).WithOne().HasForeignKey<OrderDetails>(d => d.Id);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.Ignore(d => d.Address);
            });
        }));

        Assert.Contains("'Order'", ex.Message);
        Assert.Contains("'OrderDetails'", ex.Message);
        Assert.Contains("'bucket.scope.orders'", ex.Message);
        Assert.Contains("table splitting", ex.Message);
        Assert.Contains("own collection", ex.Message);
        Assert.Contains("owned type", ex.Message);
        Assert.DoesNotContain("ComplexProperty", ex.Message);
    }

    [Fact]
    public void TableSplitting_DefaultCollectionName_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasOne(o => o.Details).WithOne().HasForeignKey<OrderDetails>(d => d.Id);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToTable("orders");
                b.Ignore(d => d.Address);
            });
        }));

        Assert.Contains("'orders'", ex.Message);
    }

    [Fact]
    public void UnrelatedTypesInOneCollection_AreRejected()
    {
        // EF Core rejects this case too, but the provider's message says what to do instead.
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.Ignore(o => o.Details);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.Ignore(d => d.Address);
            });
        }));

        Assert.Contains("'Order'", ex.Message);
        Assert.Contains("'OrderDetails'", ex.Message);
        Assert.Contains("table splitting", ex.Message);
    }

    [Fact]
    public void SharedTypeEntityTypes_InTheOwnersCollection_AreRejected()
    {
        // The shape of EF Core's ComplexNavigationsSharedTypeQueryRelationalFixtureBase: shared-type
        // entity types of one CLR type, linked by their primary keys, all in the root's table.
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<LevelOne>().ToTable("Level1");
            mb.SharedTypeEntity<Level>("Level2", b =>
            {
                b.ToTable("Level1");
                b.HasKey(l => l.Id);
                b.Property(l => l.Name).IsRequired();
                b.HasOne<LevelOne>().WithOne().HasForeignKey("Level2", nameof(Level.Id));
            });
            mb.SharedTypeEntity<Level>("Level3", b =>
            {
                b.ToTable("Level1");
                b.HasKey(l => l.Id);
                b.Property(l => l.Name).IsRequired();
                b.HasOne("Level2", null).WithOne().HasForeignKey("Level3", nameof(Level.Id));
            });
        }));

        Assert.Contains("'LevelOne'", ex.Message);
        Assert.Contains("'Level2", ex.Message);
        Assert.Contains("'Level3", ex.Message);
        Assert.Contains("'Level1'", ex.Message);
    }

    [Fact]
    public void TableSplitting_OwnedTypeOfTheDependent_NamesOnlyTheSplitTypes()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasOne(o => o.Details).WithOne().HasForeignKey<OrderDetails>(d => d.Id);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToTable("orders");
                // EF Core rejects an optional dependent in a shared table that has only nullable
                // properties and contains other dependents; a required property avoids that.
                b.Property(d => d.Notes).IsRequired();
                b.OwnsOne(d => d.Address, a => a.Ignore(x => x.Geo));
            });
        }));

        Assert.Contains("'Order', 'OrderDetails'", ex.Message);
        Assert.DoesNotContain("Address", ex.Message);
    }

    [Fact]
    public void TableSplitting_BareAndFullCollectionNames_AreTheSameCollection()
    {
        // 'orders' is resolved with the context's bucket and scope, so it is 'bucket.scope.orders'.
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasOne(o => o.Details).WithOne().HasForeignKey<OrderDetails>(d => d.Id);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.Ignore(d => d.Address);
            });
        }, bucket: "bucket", scope: "scope"));

        Assert.Contains("'Order', 'OrderDetails'", ex.Message);
        Assert.Contains("'bucket.scope.orders'", ex.Message);
    }

    [Fact]
    public void BareNameInAnotherScope_IsAccepted()
    {
        var model = BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasOne(o => o.Details).WithOne().HasForeignKey<OrderDetails>(d => d.Id);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToCouchbaseCollection("bucket", "otherScope", "orders");
                b.Ignore(d => d.Address);
            });
        }, bucket: "bucket", scope: "scope");

        Assert.NotNull(model.FindEntityType(typeof(OrderDetails)));
    }

    [Fact]
    public void Tpt_UseTptMappingStrategy_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Animal>(b =>
            {
                b.UseTptMappingStrategy();
                b.ToCouchbaseCollection("bucket", "scope", "animals");
            });
            mb.Entity<Cat>().ToCouchbaseCollection("bucket", "scope", "cats");
            mb.Entity<Dog>().ToCouchbaseCollection("bucket", "scope", "dogs");
        }));

        Assert.Contains("hierarchy of 'Animal'", ex.Message);
        Assert.Contains("'Dog' to 'bucket.scope.dogs'", ex.Message);
        Assert.Contains("table-per-type (TPT)", ex.Message);
        Assert.Contains("table-per-hierarchy (TPH)", ex.Message);
    }

    [Fact]
    public void Tpt_DerivedTypeInItsOwnCollectionWithoutStrategy_IsRejected()
    {
        // EF Core maps a derived type with a table of its own as TPT when no strategy is set.
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(mb =>
        {
            mb.Entity<Animal>().ToTable("animals");
            mb.Entity<Cat>().ToTable("animals");
            mb.Entity<Dog>().ToTable("dogs");
        }));

        Assert.Contains("table-per-type (TPT)", ex.Message);
    }

    [Fact]
    public void Tpc_EachConcreteTypeInItsOwnCollection_IsAccepted()
    {
        var model = BuildModel(mb =>
        {
            mb.Entity<Animal>(b =>
            {
                b.UseTpcMappingStrategy();
                b.ToTable("animals");
                b.Property(a => a.Id).ValueGeneratedNever();
            });
            mb.Entity<Cat>().ToTable("cats");
            mb.Entity<Dog>().ToTable("dogs");
        });

        Assert.Equal("dogs", model.FindEntityType(typeof(Dog))!.GetTableName());
    }

    [Fact]
    public void Tph_HierarchyInOneCollection_IsAccepted()
    {
        var model = BuildModel(mb =>
        {
            mb.Entity<Animal>().ToCouchbaseCollection("bucket", "scope", "animals");
            mb.Entity<Cat>().ToCouchbaseCollection("bucket", "scope", "animals");
            mb.Entity<Dog>().ToCouchbaseCollection("bucket", "scope", "animals");
        });

        Assert.Equal("bucket.scope.animals", model.FindEntityType(typeof(Dog))!.GetTableName());
    }

    [Fact]
    public void OwnsOne_InTheOwnersCollection_IsAccepted()
    {
        var model = BuildModel(mb => mb.Entity<Customer>(b =>
        {
            b.ToCouchbaseCollection("bucket", "scope", "customers");
            // EF Core needs a required navigation to tell a nested owned type with only nullable
            // properties from a missing one.
            b.OwnsOne(c => c.Address, a => a.OwnsOne(x => x.Geo));
            b.Navigation(c => c.Address).IsRequired();
            b.Ignore(c => c.Addresses);
        }));

        var owned = model.FindEntityType(typeof(Address))!;
        Assert.True(owned.IsOwned());
        Assert.Equal("bucket.scope.customers", owned.GetTableName());
    }

    [Fact]
    public void OwnsMany_IsAccepted()
    {
        var model = BuildModel(mb => mb.Entity<Customer>(b =>
        {
            b.ToCouchbaseCollection("bucket", "scope", "customers");
            b.Ignore(c => c.Address);
            b.OwnsMany(c => c.Addresses, a => a.Ignore(x => x.Geo));
        }));

        Assert.True(model.FindEntityType(typeof(Address))!.IsOwned());
    }

    [Fact]
    public void OneToOne_InDifferentCollections_IsAccepted()
    {
        var model = BuildModel(mb =>
        {
            mb.Entity<Order>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orders");
                b.HasOne(o => o.Details).WithOne().HasForeignKey<OrderDetails>(d => d.Id);
            });
            mb.Entity<OrderDetails>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "orderDetails");
                b.Ignore(d => d.Address);
            });
        });

        Assert.NotNull(model.FindEntityType(typeof(OrderDetails)));
    }

    [Fact]
    public void ManyToMany_IsAccepted()
    {
        var model = BuildModel(mb =>
        {
            mb.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "posts");
                b.HasMany(p => p.Tags).WithMany(t => t.Posts);
            });
            mb.Entity<Tag>().ToCouchbaseCollection("bucket", "scope", "tags");
        });

        Assert.Contains(model.GetEntityTypes(), e => e.HasSharedClrType && !e.IsOwned());
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel(
        Action<ModelBuilder> configure, string? bucket = null, string? scope = null)
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<ModelContext>();
        builder.UseCouchbaseProvider(clusterOptions, o =>
        {
            if (bucket != null) o.Bucket = bucket;
            if (scope != null) o.Scope = scope;
        });
        // Each test builds its own model, so the model cache must not hand back another one.
        builder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, NoCacheKeyFactory>();
        using var context = new ModelContext(builder.Options, configure);
        return context.Model;
    }

    private sealed class NoCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new object();
    }

    private sealed class ModelContext(DbContextOptions<ModelContext> options, Action<ModelBuilder> configure)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    }

    private class Order
    {
        public int Id { get; set; }
        public string? Number { get; set; }
        public OrderDetails? Details { get; set; }
    }

    private class OrderDetails
    {
        public int Id { get; set; }
        public string? Notes { get; set; }
        public Address? Address { get; set; }
    }

    private class LevelOne
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private class Level
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private class Animal
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private class Cat : Animal
    {
        public bool Indoor { get; set; }
    }

    private class Dog : Animal
    {
        public string? Breed { get; set; }
    }

    private class Customer
    {
        public int Id { get; set; }
        public Address? Address { get; set; }
        public List<Address> Addresses { get; set; } = [];
    }

    private class Address
    {
        public string? Street { get; set; }
        public Geo? Geo { get; set; }
    }

    private class Geo
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    private class Post
    {
        public int Id { get; set; }
        public List<Tag> Tags { get; set; } = [];
    }

    private class Tag
    {
        public int Id { get; set; }
        public List<Post> Posts { get; set; } = [];
    }
}
