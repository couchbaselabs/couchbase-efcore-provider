// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Records the original collection-object reference and per-item property value snapshots
/// for every OwnsMany navigation on a freshly materialised entity, so that
/// <c>CouchbaseSaveChangesInterceptor</c> can detect mutations at <c>SaveChanges</c> time.
/// <para>
/// Two kinds of change are tracked:
/// <list type="bullet">
///   <item><description>
///     <b>Reference replacement</b> — <c>customer.ContactMethods = []</c> — detected by
///     comparing the current collection object reference against the snapshotted one.
///   </description></item>
///   <item><description>
///     <b>In-place mutation</b> — <c>.Add()</c>, <c>.Remove()</c>, or scalar property
///     change — detected by comparing per-item property values against snapshotted ones.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// The underlying storage (<see cref="OwnedCollectionSnapshot.OriginalRefs"/> /
/// <see cref="OwnedCollectionSnapshot.OriginalItems"/>) uses
/// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/> so
/// entities that fall out of scope are GC'd without manual cleanup.
/// </para>
/// <para>
/// This class extracted from <c>CouchbaseQueryEnumerable&lt;T&gt;.SnapshotCollectionRefs</c>
/// so it can be unit-tested independently and reused from other materialisation paths
/// (e.g. <c>Find</c>, <c>FromSqlRaw</c>) in the future.
/// </para>
/// </summary>
internal sealed class CouchbaseCollectionSnapshot
{
    /// <summary>
    /// Records the current state of every OwnsMany navigation in
    /// <paramref name="ownedCollections"/> on <paramref name="entity"/>.
    /// </summary>
    /// <param name="entity">The freshly materialised root entity.</param>
    /// <param name="ownedCollections">The owned-collection navigations to snapshot.</param>
    /// <param name="isTracking">
    /// <see langword="true"/> for <c>TrackAll</c> queries; <see langword="false"/> for
    /// <c>NoTracking</c> / <c>NoTrackingWithIdentityResolution</c>. Snapshots are skipped
    /// for non-tracking queries because the interceptor walks <c>ChangeTracker.Entries()</c>
    /// and would never consume them.
    /// </param>
    public void Record<T>(T entity, IReadOnlyList<INavigation> ownedCollections, bool isTracking)
    {
        if (entity == null || !isTracking) return;

        var refs  = OwnedCollectionSnapshot.OriginalRefs.GetOrCreateValue(entity);
        var items = OwnedCollectionSnapshot.OriginalItems.GetOrCreateValue(entity);

        foreach (var nav in ownedCollections)
        {
            var currentCollection = nav.PropertyInfo != null
                ? nav.PropertyInfo.GetValue(entity)
                : nav.FieldInfo?.GetValue(entity);
            refs[nav.Name] = currentCollection;

            // Snapshot per-item property values so in-place mutations can be detected
            // even when the list reference is unchanged.
            if (currentCollection is IEnumerable collection)
            {
                var itemProps  = OwnedCollectionSnapshot.GetTrackedProperties(nav);
                var snapshot   = new List<Dictionary<string, object?>>();

                foreach (var item in collection)
                {
                    if (item == null) continue;
                    var propSnapshot = new Dictionary<string, object?>();
                    foreach (var prop in itemProps)
                    {
                        var raw = prop.PropertyInfo?.GetValue(item);
                        // Use EF Core's ValueComparer.Snapshot so mutable reference types
                        // (e.g. byte[]) are deep-copied; for immutable types it is a no-op.
                        propSnapshot[prop.Name] = raw is null
                            ? null
                            : OwnedCollectionSnapshot.GetComparer(prop).Snapshot(raw);
                    }
                    snapshot.Add(propSnapshot);
                }
                items[nav.Name] = snapshot;
            }
            else
            {
                items[nav.Name] = [];
            }
        }
    }
}
