using Couchbase.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Unit tests for <see cref="CouchbaseCollectionSnapshot"/>.
/// These tests exercise snapshot recording in isolation using mock INavigation objects
/// and verify that <see cref="OwnedCollectionSnapshot"/> is populated correctly.
/// </summary>
public class CouchbaseCollectionSnapshotTests
{
    private readonly CouchbaseCollectionSnapshot _sut = new();

    // -------------------------------------------------------------------------
    // Helper model
    // -------------------------------------------------------------------------

    private class Owner
    {
        public List<Item> Items { get; set; } = [];
    }

    private class Item
    {
        public string? Value { get; set; }
        public int Count { get; set; }
    }

    // Build a minimal mock INavigation pointing at Owner.Items
    private static INavigation BuildNavigation()
    {
        var itemProp = typeof(Owner).GetProperty(nameof(Owner.Items))!;

        var itemValueProp = typeof(Item).GetProperty(nameof(Item.Value))!;
        var itemCountProp = typeof(Item).GetProperty(nameof(Item.Count))!;

        var mockValueProp = new Mock<IProperty>();
        mockValueProp.Setup(p => p.Name).Returns(nameof(Item.Value));
        mockValueProp.Setup(p => p.PropertyInfo).Returns(itemValueProp);
        mockValueProp.Setup(p => p.ClrType).Returns(typeof(string));
        mockValueProp.Setup(p => p.IsShadowProperty()).Returns(false);
        mockValueProp.Setup(p => p.GetValueComparer()).Returns((Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer?)null);
        mockValueProp.Setup(p => p.FindTypeMapping()).Returns((Microsoft.EntityFrameworkCore.Storage.CoreTypeMapping?)null);

        var mockCountProp = new Mock<IProperty>();
        mockCountProp.Setup(p => p.Name).Returns(nameof(Item.Count));
        mockCountProp.Setup(p => p.PropertyInfo).Returns(itemCountProp);
        mockCountProp.Setup(p => p.ClrType).Returns(typeof(int));
        mockCountProp.Setup(p => p.IsShadowProperty()).Returns(false);
        mockCountProp.Setup(p => p.GetValueComparer()).Returns((Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer?)null);
        mockCountProp.Setup(p => p.FindTypeMapping()).Returns((Microsoft.EntityFrameworkCore.Storage.CoreTypeMapping?)null);

        var mockEntityType = new Mock<IEntityType>();
        mockEntityType.Setup(et => et.GetProperties())
            .Returns([mockValueProp.Object, mockCountProp.Object]);

        var mockNav = new Mock<INavigation>();
        mockNav.Setup(n => n.Name).Returns(nameof(Owner.Items));
        mockNav.Setup(n => n.PropertyInfo).Returns(itemProp);
        mockNav.Setup(n => n.TargetEntityType).Returns(mockEntityType.Object);

        return mockNav.Object;
    }

    // -------------------------------------------------------------------------
    // isTracking = false → no snapshot recorded
    // -------------------------------------------------------------------------

    [Fact]
    public void Record_NonTracking_DoesNotSnapshot()
    {
        var owner = new Owner { Items = [new Item { Value = "x", Count = 1 }] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: false);

        Assert.False(OwnedCollectionSnapshot.OriginalRefs.TryGetValue(owner, out _));
    }

    [Fact]
    public void Record_NullEntity_DoesNotThrow()
    {
        var nav = BuildNavigation();
        var ex = Record.Exception(() => _sut.Record<Owner>(null!, [nav], isTracking: true));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Reference replacement detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Record_Tracking_SnapshotsCollectionReference()
    {
        var owner = new Owner { Items = [new Item { Value = "a", Count = 1 }] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        Assert.True(OwnedCollectionSnapshot.OriginalRefs.TryGetValue(owner, out var refs));
        Assert.Same(owner.Items, refs![nameof(Owner.Items)]);
    }

    [Fact]
    public void Record_Tracking_DetectsReferenceReplacement()
    {
        var owner = new Owner { Items = [new Item { Value = "a", Count = 1 }] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        // Replace the collection reference
        var originalRef = owner.Items;
        owner.Items = [new Item { Value = "b", Count = 2 }];

        OwnedCollectionSnapshot.OriginalRefs.TryGetValue(owner, out var refs);
        Assert.Same(originalRef, refs![nameof(Owner.Items)]);
        Assert.NotSame(owner.Items, refs[nameof(Owner.Items)]);
    }

    // -------------------------------------------------------------------------
    // In-place mutation detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Record_Tracking_SnapshotsItemPropertyValues()
    {
        var item = new Item { Value = "hello", Count = 42 };
        var owner = new Owner { Items = [item] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        Assert.True(OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var items));
        var snapshots = items![nameof(Owner.Items)];
        Assert.Single(snapshots);
        Assert.Equal("hello", snapshots[0][nameof(Item.Value)]);
        Assert.Equal(42, snapshots[0][nameof(Item.Count)]);
    }

    [Fact]
    public void Record_Tracking_DetectsScalarPropertyMutation()
    {
        var item = new Item { Value = "original", Count = 1 };
        var owner = new Owner { Items = [item] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        // Mutate in-place — snapshot still holds original value
        item.Value = "mutated";

        OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var items);
        Assert.Equal("original", items![nameof(Owner.Items)][0][nameof(Item.Value)]);
    }

    // -------------------------------------------------------------------------
    // Item count change detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Record_Tracking_EmptyCollection_SnapshotsEmptyList()
    {
        var owner = new Owner { Items = [] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        Assert.True(OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var items));
        Assert.Empty(items![nameof(Owner.Items)]);
    }

    [Fact]
    public void Record_Tracking_DetectsItemAdded()
    {
        var owner = new Owner { Items = [new Item { Value = "a", Count = 1 }] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        // Add an item after snapshot
        owner.Items.Add(new Item { Value = "b", Count = 2 });

        OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var items);
        // Snapshot captured 1 item; current collection has 2
        Assert.Single(items![nameof(Owner.Items)]);
        Assert.Equal(2, owner.Items.Count);
    }

    // -------------------------------------------------------------------------
    // No-op second save (snapshot is fresh after first save)
    // -------------------------------------------------------------------------

    [Fact]
    public void Record_CalledTwice_SecondCallOverwritesSnapshot()
    {
        var item = new Item { Value = "v1", Count = 1 };
        var owner = new Owner { Items = [item] };
        var nav = BuildNavigation();

        _sut.Record(owner, [nav], isTracking: true);

        // Simulate save + refresh: mutate and re-snapshot
        item.Value = "v2";
        _sut.Record(owner, [nav], isTracking: true);

        OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var items);
        // Second snapshot should capture the updated value
        Assert.Equal("v2", items![nameof(Owner.Items)][0][nameof(Item.Value)]);
    }
}
