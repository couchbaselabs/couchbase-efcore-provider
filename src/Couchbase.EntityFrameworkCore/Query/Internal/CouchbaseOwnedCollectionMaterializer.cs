// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Populates owned-collection navigations on a freshly materialised entity from the
/// embedded JSON arrays that arrive inline in a Couchbase N1QL result row.
/// <para>
/// This class owns three concerns that previously lived in
/// <see cref="CouchbaseQueryEnumerable{T}"/>:
/// <list type="bullet">
///   <item><description>
///     <see cref="Populate{T}"/> — iterates every OwnsMany navigation on the root entity
///     and delegates to <see cref="MaterializeOwnedItem"/> for each array element.
///   </description></item>
///   <item><description>
///     <see cref="MaterializeOwnedItem"/> — recursively materialises a single owned entity
///     at any nesting depth (OwnsOne-within-OwnsMany, OwnsMany-within-OwnsMany, etc.).
///   </description></item>
///   <item><description>
///     <see cref="ConvertJsonValue"/> — maps a <see cref="JsonElement"/> to a CLR scalar
///     value, respecting nullability and common primitive types.
///   </description></item>
/// </list>
/// </para>
/// </summary>
internal sealed class CouchbaseOwnedCollectionMaterializer
{
    private static readonly JsonSerializerOptions _defaultSerializerOptions =
        new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------------------
    // Public entry point
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Populates every OwnsMany navigation in <paramref name="ownedCollections"/> on
    /// <paramref name="entity"/> from the corresponding JSON array in
    /// <paramref name="docElement"/>.
    /// </summary>
    public void Populate<T>(
        T entity,
        JsonElement docElement,
        IReadOnlyList<INavigation> ownedCollections,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions? serializerOptions)
    {
        var options = serializerOptions ?? _defaultSerializerOptions;
        foreach (var nav in ownedCollections)
        {
            var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;
            if (!docElement.TryGetPropertyCI(fieldName, out var arrayElement)
                || arrayElement.ValueKind != JsonValueKind.Array)
                continue;

            var accessor = nav.GetCollectionAccessor();
            var clrType  = nav.TargetEntityType.ClrType;

            if (accessor != null)
            {
                // Clear any items the EF Core shaper may have pre-populated from the
                // injected OwnsMany column (observed with AsNoTracking queries).
                // We are the authoritative source for owned-collection data; clearing
                // before adding prevents duplicates.
                // Use IList fast-path; fall back to ICollection<T> so that non-IList
                // types (HashSet<T>, SortedSet<T>, etc.) are also emptied correctly.
                var coll = accessor.GetOrCreate(entity!, forMaterialization: true);
                if (coll is IList listColl)
                    listColl.Clear();
                else if (coll != null)
                    typeof(ICollection<>).MakeGenericType(clrType)
                        .GetMethod("Clear")!
                        .Invoke(coll, null);
            }
            else
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(clrType))!;
                nav.PropertyInfo?.SetValue(entity, list);
            }

            foreach (var itemElement in arrayElement.EnumerateArray())
            {
                var ownedEntity = MaterializeOwnedItem(itemElement, nav.TargetEntityType, fieldNamingPolicy, options);
                if (accessor != null)
                    accessor.Add(entity!, ownedEntity, forMaterialization: true);
                else
                    ((IList)nav.PropertyInfo!.GetValue(entity)!).Add(ownedEntity);
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Recursive owned-item materialiser
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Materialises a single owned entity from a <see cref="JsonElement"/> by setting its
    /// scalar properties and recursively populating any nested owned navigations
    /// (OwnsOne / OwnsMany at any depth).
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal static</c> so unit tests can call it directly without
    /// constructing a full materialiser instance.
    /// </remarks>
    internal static object MaterializeOwnedItem(
        JsonElement itemElement,
        IEntityType entityType,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions options)
    {
        var ownedEntity = Activator.CreateInstance(entityType.ClrType)!;

        // Scalar properties
        foreach (var prop in entityType.GetProperties())
        {
            if (prop.IsShadowProperty()) continue;
            var jsonKey = fieldNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
            if (itemElement.TryGetPropertyCI(jsonKey, out var propElement))
                prop.PropertyInfo?.SetValue(ownedEntity, ConvertJsonValue(propElement, prop.ClrType, options));
        }

        // Nested owned navigations
        foreach (var ownedNav in entityType.GetNavigations())
        {
            if (!ownedNav.TargetEntityType.IsOwned()) continue;
            var fieldName = fieldNamingPolicy?.ConvertName(ownedNav.Name) ?? ownedNav.Name;
            if (!itemElement.TryGetPropertyCI(fieldName, out var nestedElement)) continue;

            if (ownedNav.IsCollection)
            {
                if (nestedElement.ValueKind != JsonValueKind.Array) continue;
                var accessor = ownedNav.GetCollectionAccessor();
                // Skip navigations with no accessor and no PropertyInfo — nothing to set.
                if (accessor == null && ownedNav.PropertyInfo == null) continue;
                var nestedClrType = ownedNav.TargetEntityType.ClrType;
                if (accessor != null)
                {
                    var coll = accessor.GetOrCreate(ownedEntity, forMaterialization: true);
                    // Clear via IList for the common List<T> case; otherwise cast through
                    // ICollection<T> which every mutable .NET collection implements and
                    // guarantees Clear() — correctly handles HashSet<T>, SortedSet<T>, etc.
                    if (coll is IList list)
                        list.Clear();
                    else if (coll != null)
                        typeof(ICollection<>).MakeGenericType(nestedClrType)
                            .GetMethod("Clear")!
                            .Invoke(coll, null);
                }
                else
                {
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(nestedClrType))!;
                    ownedNav.PropertyInfo!.SetValue(ownedEntity, list);
                }
                foreach (var nestedItemElement in nestedElement.EnumerateArray())
                {
                    var nestedEntity = MaterializeOwnedItem(nestedItemElement, ownedNav.TargetEntityType, fieldNamingPolicy, options);
                    if (accessor != null)
                        accessor.Add(ownedEntity, nestedEntity, forMaterialization: true);
                    else
                    {
                        // PropertyInfo is guaranteed non-null here: we skipped above when both
                        // accessor and PropertyInfo are null, and accessor is null in this branch.
                        var existingList = (IList)ownedNav.PropertyInfo!.GetValue(ownedEntity)!;
                        existingList.Add(nestedEntity);
                    }
                }
            }
            else
            {
                if (nestedElement.ValueKind != JsonValueKind.Object) continue;
                // Skip shadow/field-only navigations — no PropertyInfo means nowhere to assign.
                if (ownedNav.PropertyInfo == null) continue;
                var nestedEntity = MaterializeOwnedItem(nestedElement, ownedNav.TargetEntityType, fieldNamingPolicy, options);
                ownedNav.PropertyInfo.SetValue(ownedEntity, nestedEntity);
            }
        }

        return ownedEntity;
    }

    // ---------------------------------------------------------------------------
    // JSON → CLR type conversion
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to the requested CLR <paramref name="targetType"/>.
    /// Handles nullable wrappers, common primitives, and falls back to
    /// <see cref="JsonSerializer.Deserialize"/> for unknown types.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal static</c> for unit testing.
    /// Note (Phase 3): this will be replaced by a lookup into
    /// <c>IProperty.FindTypeMapping()?.JsonValueReaderWriter</c> / <c>IProperty.GetValueConverter()</c>
    /// so that EF Core value converters and custom type mappings are respected.
    /// </remarks>
    internal static object? ConvertJsonValue(JsonElement element, Type targetType, JsonSerializerOptions options)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return t switch
        {
            _ when t == typeof(string)   => element.GetString(),
            _ when t == typeof(int)      => element.GetInt32(),
            _ when t == typeof(long)     => element.GetInt64(),
            _ when t == typeof(double)   => element.GetDouble(),
            _ when t == typeof(decimal)  => element.GetDecimal(),
            _ when t == typeof(float)    => (float)element.GetDouble(),
            _ when t == typeof(bool)     => element.GetBoolean(),
            _ when t == typeof(Guid)     => element.GetGuid(),
            _ when t == typeof(DateTime) => element.GetDateTime(),
            _ => JsonSerializer.Deserialize(element, t, options)
        };
    }
}
