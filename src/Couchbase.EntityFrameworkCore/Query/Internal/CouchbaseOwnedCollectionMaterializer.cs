// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.Json;

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
                // Use the property setter when one exists; fall back to FieldInfo for
                // backing-field navigations where PropertyInfo is null or has no setter.
                if (nav.PropertyInfo?.GetSetMethod(nonPublic: true) != null)
                    nav.PropertyInfo.SetValue(entity, list);
                else if (nav.FieldInfo != null)
                    nav.FieldInfo.SetValue(entity, list);
            }

            foreach (var itemElement in arrayElement.EnumerateArray())
            {
                var ownedEntity = MaterializeOwnedItem(itemElement, nav.TargetEntityType, fieldNamingPolicy, options);
                if (accessor != null)
                    accessor.Add(entity!, ownedEntity, forMaterialization: true);
                else
                {
                    // Read the list back via the same PropertyInfo → FieldInfo fallback.
                    var existingList = (IList?)(nav.PropertyInfo != null
                        ? nav.PropertyInfo.GetValue(entity)
                        : nav.FieldInfo?.GetValue(entity));
                    existingList?.Add(ownedEntity);
                }
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
            {
                var converted = ConvertFromJson(propElement, prop, options);
                // Use the property setter when one exists (public or non-public).
                // Fall back to FieldInfo for backing-field / field-access properties where
                // PropertyInfo is null or has no setter (e.g. init-only / get-only).
                if (prop.PropertyInfo?.GetSetMethod(nonPublic: true) != null)
                    prop.PropertyInfo.SetValue(ownedEntity, converted);
                else if (prop.FieldInfo != null)
                    prop.FieldInfo.SetValue(ownedEntity, converted);
            }
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
                // Skip navigations with no accessor and no way to assign — nothing to set.
                if (accessor == null && ownedNav.PropertyInfo == null && ownedNav.FieldInfo == null) continue;
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
                    // Use the property setter when one exists; fall back to FieldInfo for
                    // backing-field navigations where PropertyInfo is null or has no setter.
                    if (ownedNav.PropertyInfo?.GetSetMethod(nonPublic: true) != null)
                        ownedNav.PropertyInfo.SetValue(ownedEntity, list);
                    else if (ownedNav.FieldInfo != null)
                        ownedNav.FieldInfo.SetValue(ownedEntity, list);
                }
                foreach (var nestedItemElement in nestedElement.EnumerateArray())
                {
                    var nestedEntity = MaterializeOwnedItem(nestedItemElement, ownedNav.TargetEntityType, fieldNamingPolicy, options);
                    if (accessor != null)
                        accessor.Add(ownedEntity, nestedEntity, forMaterialization: true);
                    else
                    {
                        // Read back via the same PropertyInfo → FieldInfo fallback.
                        var existingList = (IList?)(ownedNav.PropertyInfo != null
                            ? ownedNav.PropertyInfo.GetValue(ownedEntity)
                            : ownedNav.FieldInfo?.GetValue(ownedEntity));
                        existingList?.Add(nestedEntity);
                    }
                }
            }
            else
            {
                if (nestedElement.ValueKind != JsonValueKind.Object) continue;
                // Skip navigations with no way to assign.
                if (ownedNav.PropertyInfo == null && ownedNav.FieldInfo == null) continue;
                var nestedEntity = MaterializeOwnedItem(nestedElement, ownedNav.TargetEntityType, fieldNamingPolicy, options);
                // Use property setter when available; fall back to FieldInfo.
                if (ownedNav.PropertyInfo?.GetSetMethod(nonPublic: true) != null)
                    ownedNav.PropertyInfo.SetValue(ownedEntity, nestedEntity);
                else if (ownedNav.FieldInfo != null)
                    ownedNav.FieldInfo.SetValue(ownedEntity, nestedEntity);
            }
        }

        return ownedEntity;
    }

    // ---------------------------------------------------------------------------
    // JSON → CLR type conversion
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to the CLR type for <paramref name="property"/>,
    /// respecting the EF Core type-mapping pipeline in priority order:
    /// <list type="number">
    ///   <item><description>
    ///     <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/> from
    ///     <c>HasConversion</c> — the JSON element is first deserialized as the provider CLR type,
    ///     then <c>ConvertFromProvider</c> converts it to the model CLR type.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="JsonValueReaderWriter"/> from the property's type mapping — used when a
    ///     custom type mapping (e.g. <c>JsonObject</c>) registers a reader.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="ConvertJsonValue"/> — the hand-rolled primitive switch, used when neither
    ///     a value converter nor a type-mapping reader is present.
    ///   </description></item>
    /// </list>
    /// </summary>
    /// </summary>
    internal static object? ConvertFromJson(JsonElement element, IProperty property, JsonSerializerOptions options)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;

        // 1. Value converter from HasConversion — checked first because it is the most
        //    common user-facing path and avoids a format mismatch with JsonValueReaderWriter
        //    (e.g. an enum stored as an integer would fail the string reader).
        //    GetValueConverter() delegates to FindTypeMapping()?.Converter in EF Core 10;
        //    we also check FindTypeMapping().Converter directly as a belt-and-suspenders
        //    fallback for owned-entity properties where the two may diverge.
        var typeMapping = property.FindTypeMapping();
        var converter   = property.GetValueConverter() ?? typeMapping?.Converter;
        if (converter != null)
        {
            var providerValue = ConvertJsonValue(element, converter.ProviderClrType, options);
            return converter.ConvertFromProvider(providerValue);
        }

        // 2. Type-mapping JsonValueReaderWriter (e.g. JsonObject, JsonArray, custom mappings
        //    that do not use a ValueConverter but register their own JSON reader).
        var readerWriter = typeMapping?.JsonValueReaderWriter;
        if (readerWriter != null)
            return readerWriter.FromJsonString(element.GetRawText(), existingObject: null);

        // 3. Primitive switch fallback.
        return ConvertJsonValue(element, property.ClrType, options);
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to the requested CLR <paramref name="targetType"/>.
    /// Handles nullable wrappers, common primitives, and falls back to
    /// <see cref="JsonSerializer.Deserialize"/> for unknown types.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal static</c> for unit testing. Used by <see cref="ConvertFromJson"/>
    /// as its primitive fallback and for deserializing value-converter provider types.
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
