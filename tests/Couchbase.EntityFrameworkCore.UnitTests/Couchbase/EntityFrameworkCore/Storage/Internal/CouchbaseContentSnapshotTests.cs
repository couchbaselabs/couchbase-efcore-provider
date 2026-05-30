using System.Reflection;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Unit tests for the Phase-5 content-snapshot mechanism.
///
/// HasCollectionChanged (private static on CouchbaseSaveChangesInterceptor) is the core
/// detection predicate; it is reached via reflection.  OwnedCollectionSnapshot.OriginalItems
/// is the ConditionalWeakTable that feeds it at runtime.
///
/// Tests are grouped into three regions:
///   1. HasCollectionChanged — pure logic, no EF Core context.
///   2. HasCollectionChanged round-trip — snapshot → mutate → detect.
///   3. OwnedCollectionSnapshot.OriginalItems — table isolation / persistence.
/// </summary>
public class CouchbaseContentSnapshotTests
{
    // -------------------------------------------------------------------------
    // CLR types used by all tests — kept deliberately minimal
    // -------------------------------------------------------------------------

    private class ContactItem
    {
        public string? Type  { get; set; }
        public string? Value { get; set; }
    }

    // -------------------------------------------------------------------------
    // Reflection entry-point
    // -------------------------------------------------------------------------

    private static readonly MethodInfo HasCollectionChangedMethod =
        typeof(CouchbaseSaveChangesInterceptor)
            .GetMethod("HasCollectionChanged", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "HasCollectionChanged not found — was the method renamed or removed?");

    /// <summary>Thin wrapper that mirrors the production signature.</summary>
    private static bool CallHasCollectionChanged(
        object? current,
        INavigation nav,
        IReadOnlyList<Dictionary<string, object?>> origItemSnapshots)
        => (bool)HasCollectionChangedMethod.Invoke(null, [current, nav, origItemSnapshots])!;

    // -------------------------------------------------------------------------
    // Mock INavigation builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a mock INavigation whose TargetEntityType returns two non-shadow
    /// IProperty instances that mirror <see cref="ContactItem.Type"/> and
    /// <see cref="ContactItem.Value"/>.
    ///
    /// IsShadowProperty() is a static extension that checks property.PropertyInfo == null;
    /// setting PropertyInfo to a real PropertyInfo makes the filter pass without mocking
    /// the extension method directly.
    /// </summary>
    private static INavigation BuildNav()
    {
        var typePropInfo  = typeof(ContactItem).GetProperty(nameof(ContactItem.Type))!;
        var valuePropInfo = typeof(ContactItem).GetProperty(nameof(ContactItem.Value))!;

        // Default string comparer: ordinal equality, null-safe.
        // GetValueComparer() is an IReadOnlyProperty interface method — Moq returns null by
        // default, so it must be set up explicitly or HasCollectionChanged will NullRef.
        var stringComparer = new ValueComparer<string?>(
            (l, r) => l == r,
            v => v == null ? 0 : v.GetHashCode());

        var typeProp = new Mock<IProperty>();
        typeProp.Setup(p => p.Name).Returns("Type");
        typeProp.Setup(p => p.PropertyInfo).Returns(typePropInfo);
        typeProp.Setup(p => p.GetValueComparer()).Returns(stringComparer);

        var valueProp = new Mock<IProperty>();
        valueProp.Setup(p => p.Name).Returns("Value");
        valueProp.Setup(p => p.PropertyInfo).Returns(valuePropInfo);
        valueProp.Setup(p => p.GetValueComparer()).Returns(stringComparer);

        var targetType = new Mock<IEntityType>();
        targetType.Setup(t => t.GetProperties()).Returns([typeProp.Object, valueProp.Object]);

        var nav = new Mock<INavigation>();
        nav.Setup(n => n.Name).Returns("ContactItems");
        nav.Setup(n => n.TargetEntityType).Returns(targetType.Object);

        return nav.Object;
    }

    // -------------------------------------------------------------------------
    // Helper: build a snapshot list from items (mirrors SnapshotCollectionRefs)
    // -------------------------------------------------------------------------

    private static List<Dictionary<string, object?>> Snap(IEnumerable<ContactItem> items)
        => items.Select(item => new Dictionary<string, object?>
        {
            ["Type"]  = item.Type,
            ["Value"] = item.Value
        }).ToList();

    // =========================================================================
    // Region 1: HasCollectionChanged — pure detection logic
    // =========================================================================

    [Fact]
    public void HasCollectionChanged_NullCurrent_ReturnsFalse()
    {
        // Guard clause: current is not IEnumerable → false immediately, no crash.
        var nav = BuildNav();
        var orig = Snap([new ContactItem { Type = "email", Value = "a@b.com" }]);

        Assert.False(CallHasCollectionChanged(null, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_NonEnumerableCurrent_ReturnsFalse()
    {
        // A scalar value (int) is definitely not IEnumerable.
        var nav = BuildNav();
        var orig = Snap([new ContactItem { Type = "email", Value = "a@b.com" }]);

        Assert.False(CallHasCollectionChanged(42, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_BothEmpty_ReturnsFalse()
    {
        var nav = BuildNav();

        Assert.False(CallHasCollectionChanged(
            new List<ContactItem>(),
            nav,
            new List<Dictionary<string, object?>>()));
    }

    [Fact]
    public void HasCollectionChanged_SameContent_SingleItem_ReturnsFalse()
    {
        var nav     = BuildNav();
        var current = new List<ContactItem>
            { new() { Type = "email", Value = "alice@example.com" } };

        Assert.False(CallHasCollectionChanged(current, nav, Snap(current)));
    }

    [Fact]
    public void HasCollectionChanged_SameContent_MultipleItems_ReturnsFalse()
    {
        var nav     = BuildNav();
        var current = new List<ContactItem>
        {
            new() { Type = "email", Value = "alice@example.com" },
            new() { Type = "phone", Value = "555-0100" }
        };

        Assert.False(CallHasCollectionChanged(current, nav, Snap(current)));
    }

    [Fact]
    public void HasCollectionChanged_BothNull_SameContent_ReturnsFalse()
    {
        // null == null should not trigger a change.
        var nav     = BuildNav();
        var current = new List<ContactItem> { new() { Type = null, Value = null } };

        Assert.False(CallHasCollectionChanged(current, nav, Snap(current)));
    }

    [Fact]
    public void HasCollectionChanged_ItemAdded_ReturnsTrue()
    {
        // .Add() on an existing list: count grows from 1 to 2.
        var nav  = BuildNav();
        var orig = Snap([new ContactItem { Type = "email", Value = "alice@example.com" }]);
        var current = new List<ContactItem>
        {
            new() { Type = "email", Value = "alice@example.com" },
            new() { Type = "phone", Value = "555-0100" }           // new item
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_ItemRemoved_ReturnsTrue()
    {
        // .Remove() on an existing list: count shrinks from 2 to 1.
        var nav = BuildNav();
        var orig = Snap(
        [
            new ContactItem { Type = "email", Value = "alice@example.com" },
            new ContactItem { Type = "phone", Value = "555-0100" }
        ]);
        var current = new List<ContactItem>
        {
            new() { Type = "phone", Value = "555-0100" }
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_PropertyMutated_Value_ReturnsTrue()
    {
        // In-place scalar mutation: same count, same Type, different Value.
        var nav  = BuildNav();
        var orig = Snap([new ContactItem { Type = "email", Value = "alice@example.com" }]);
        var current = new List<ContactItem>
        {
            new() { Type = "email", Value = "alice.new@example.com" }   // mutated
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_PropertyMutated_Type_ReturnsTrue()
    {
        // Only the first property differs; the second is unchanged.
        var nav  = BuildNav();
        var orig = Snap([new ContactItem { Type = "email", Value = "alice@example.com" }]);
        var current = new List<ContactItem>
        {
            new() { Type = "sms", Value = "alice@example.com" }          // Type mutated
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_NullToNonNull_ReturnsTrue()
    {
        // Property was null at load time; user sets it before save.
        var nav  = BuildNav();
        var orig = Snap([new ContactItem { Type = null, Value = "alice@example.com" }]);
        var current = new List<ContactItem>
        {
            new() { Type = "email", Value = "alice@example.com" }        // null → "email"
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_NonNullToNull_ReturnsTrue()
    {
        // Property set to null after load.
        var nav  = BuildNav();
        var orig = Snap([new ContactItem { Type = "email", Value = "alice@example.com" }]);
        var current = new List<ContactItem>
        {
            new() { Type = null, Value = "alice@example.com" }           // "email" → null
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void HasCollectionChanged_SnapshotMissingProperty_ReturnsTrue()
    {
        // If the snapshot dict has no key for a property we conservatively report a
        // change rather than silently swallowing potential data loss.
        var nav     = BuildNav(); // expects "Type" and "Value" keys
        var current = new List<ContactItem>
            { new() { Type = "email", Value = "a@b.com" } };
        var orig = new List<Dictionary<string, object?>>
        {
            new() { /* intentionally empty — missing both keys */ }
        };

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    // =========================================================================
    // Region 2: Round-trip — snapshot taken, object mutated in-place, detected
    // =========================================================================

    [Fact]
    public void RoundTrip_SnapshotTaken_ThenPropertyMutated_Detected()
    {
        // Simulates the full lifecycle:
        //   (a) Snapshot taken at load time — item.Value = "alice@example.com"
        //   (b) User mutates item.Value in-place (same object, same list reference)
        //   (c) HasCollectionChanged detects the change
        var nav  = BuildNav();
        var item = new ContactItem { Type = "email", Value = "alice@example.com" };
        var current = new List<ContactItem> { item };
        var orig = Snap(current);   // snapshot taken at load time with old value

        // Simulate in-place mutation (same object, same list)
        item.Value = "alice.mutated@example.com";

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void RoundTrip_SnapshotTaken_NoMutation_NotDetected()
    {
        // After a snapshot is taken, calling HasCollectionChanged on unchanged data
        // must return false — no spurious dirty detection.
        var nav     = BuildNav();
        var current = new List<ContactItem>
        {
            new() { Type = "email", Value = "alice@example.com" },
            new() { Type = "phone", Value = "555-0100" }
        };
        var orig = Snap(current);   // snapshot taken; nothing changes

        Assert.False(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void RoundTrip_SnapshotTaken_ItemAdded_Detected()
    {
        // .Add() path: snapshot has 1 item, collection now has 2.
        var nav  = BuildNav();
        var item = new ContactItem { Type = "email", Value = "alice@example.com" };
        var current = new List<ContactItem> { item };
        var orig = Snap(current);   // snapshot: 1 item

        current.Add(new ContactItem { Type = "phone", Value = "555-0100" });

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    [Fact]
    public void RoundTrip_SnapshotTaken_ItemRemoved_Detected()
    {
        // .Remove() path: snapshot has 2 items, collection now has 1.
        var nav    = BuildNav();
        var email  = new ContactItem { Type = "email", Value = "alice@example.com" };
        var phone  = new ContactItem { Type = "phone", Value = "555-0100" };
        var current = new List<ContactItem> { email, phone };
        var orig = Snap(current);   // snapshot: 2 items

        current.Remove(email);

        Assert.True(CallHasCollectionChanged(current, nav, orig));
    }

    // =========================================================================
    // Region 3: OwnedCollectionSnapshot.OriginalItems — table behaviour
    // =========================================================================

    [Fact]
    public void OriginalItems_TwoOwners_HaveIndependentEntries()
    {
        // Entries for distinct owner objects must not bleed into each other.
        var owner1 = new object();
        var owner2 = new object();

        var t1 = OwnedCollectionSnapshot.OriginalItems.GetOrCreateValue(owner1);
        t1["Nav"] = [new Dictionary<string, object?> { ["x"] = "A" }];

        var t2 = OwnedCollectionSnapshot.OriginalItems.GetOrCreateValue(owner2);
        t2["Nav"] = [new Dictionary<string, object?> { ["x"] = "B" }];

        Assert.True(OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner1, out var r1));
        Assert.True(OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner2, out var r2));
        Assert.Equal("A", r1!["Nav"][0]["x"]);
        Assert.Equal("B", r2!["Nav"][0]["x"]);
    }

    [Fact]
    public void OriginalItems_UpdateExistingEntry_OverwritesPreviousSnapshot()
    {
        // Simulates what RefreshOwnedCollectionSnapshots does after a successful save:
        // the nav entry is replaced with the new snapshot, so the next save sees a
        // fresh baseline and does not falsely re-detect the just-written mutation.
        var owner = new object();
        var table = OwnedCollectionSnapshot.OriginalItems.GetOrCreateValue(owner);

        // First snapshot (at load time)
        table["Nav"] = [new Dictionary<string, object?> { ["Value"] = "old" }];

        // After save, RefreshOwnedCollectionSnapshots replaces the entry
        table["Nav"] = [new Dictionary<string, object?> { ["Value"] = "new" }];

        Assert.True(OwnedCollectionSnapshot.OriginalItems.TryGetValue(owner, out var r));
        Assert.Equal("new", r!["Nav"][0]["Value"]);
    }

    [Fact]
    public void OriginalItems_EntityNotInTable_TryGetValue_ReturnsFalse()
    {
        // An entity that was never snapshotted (e.g. newly Added) must not be present
        // in the table — the interceptor should skip content-change detection for it.
        var freshOwner = new object();

        Assert.False(OwnedCollectionSnapshot.OriginalItems.TryGetValue(freshOwner, out _));
    }
}
