// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Populates owned navigations on a freshly materialised entity from the embedded JSON that
/// arrives inline in a Couchbase N1QL result row.
/// <para>
/// This class owns three concerns that previously lived in
/// <see cref="CouchbaseQueryEnumerable{T}"/>:
/// <list type="bullet">
///   <item><description>
///     <see cref="Populate{T}"/> — iterates every OwnsMany navigation on the root entity
///     and delegates to <see cref="MaterializeOwnedItem"/> for each array element.
///   </description></item>
///   <item><description>
///     <see cref="PopulateReference{T}"/> — for a root-level OwnsOne navigation whose data is
///     stored as a genuinely nested JSON object (rather than this provider's default flat
///     table-split columns), overrides the navigation's properties <em>in place</em> on the
///     shaper's already-materialised instance. Absent/null data is left alone, so this is a
///     pure fallback — it never disturbs the flat-column round-trip most OwnsOne data already
///     uses.
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

    /// <summary>
    /// A scalar property that <see cref="PopulateReference{T}"/> overrode on an existing owned
    /// instance, reported so the caller can realign the property's tracked "original value" (EF
    /// Core's own change detection would otherwise see the override as a user-driven mutation and
    /// mark the owner spuriously <c>Modified</c> on the next <c>SaveChanges</c> — the owned
    /// instance's properties are ordinary table-split, snapshot-tracked properties, unlike
    /// OwnsMany items, which fall outside EF's own change detection entirely).
    /// </summary>
    public readonly record struct TouchedProperty(object Instance, IProperty Property);

    /// <summary>
    /// Resolves a root-level owned navigation's actual N1QL result-row key: the compile-time
    /// resolved alias when available (see <see cref="CouchbaseProjectionAliases.NavigationKey"/>),
    /// otherwise the policy-computed field name. Only root-level lookups need this — a nested
    /// navigation's field is read from within an already-extracted JSON sub-object, which was
    /// never subject to top-level SELECT alias uniquification in the first place.
    /// </summary>
    private static string ResolveRootFieldName(
        INavigation nav, IReadOnlyDictionary<string, string>? navigationAliases, JsonNamingPolicy? fieldNamingPolicy)
        => navigationAliases != null
            && navigationAliases.TryGetValue(CouchbaseProjectionAliases.NavigationKey(nav), out var alias)
            ? alias
            : fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;

    // ---------------------------------------------------------------------------
    // Public entry point
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Populates every OwnsMany navigation in <paramref name="ownedCollections"/> on
    /// <paramref name="entity"/> from the corresponding JSON array in
    /// <paramref name="docElement"/>.
    /// </summary>
    /// <param name="navigationAliases">
    /// Maps <see cref="CouchbaseProjectionAliases.NavigationKey"/> to the navigation's actual
    /// N1QL result-row key, resolved at compile time once alias uniquification has run — a plain
    /// policy-computed field name can be stale if it collided with another projected column and
    /// was suffixed (e.g. "reviews" → "reviews0"). Falls back to the policy-computed name for any
    /// navigation not present in the map (e.g. when called without a compiled query, such as in
    /// unit tests).
    /// </param>
    public void Populate<T>(
        T entity,
        JsonElement docElement,
        IReadOnlyList<INavigation> ownedCollections,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions? serializerOptions,
        IReadOnlyDictionary<string, string>? navigationAliases = null)
    {
        var options = serializerOptions ?? _defaultSerializerOptions;
        foreach (var nav in ownedCollections)
        {
            var fieldName = ResolveRootFieldName(nav, navigationAliases, fieldNamingPolicy);
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

    /// <summary>
    /// For each root-level OwnsOne navigation in <paramref name="ownedReferences"/>, overrides
    /// the navigation's scalar properties <em>in place</em> from the corresponding nested JSON
    /// object in <paramref name="docElement"/> — but only when that field is actually present as
    /// a JSON object. When it's absent or null (the common case: a document this provider wrote
    /// itself, using flat table-split columns), the shaper's already-materialised flat-column
    /// result is left completely untouched.
    /// </summary>
    /// <returns>
    /// Every scalar property actually overridden, across all navigations and any nested OwnsOne
    /// depth, so the caller can realign EF Core's own change-tracking snapshot for each — see
    /// <see cref="TouchedProperty"/>.
    /// </returns>
    /// <param name="navigationAliases">
    /// See <see cref="Populate{T}"/>'s parameter of the same name — the same alias-resolution
    /// concern applies here, since this navigation's field is also read directly off the
    /// top-level SELECT-driven <paramref name="docElement"/>.
    /// </param>
    public IReadOnlyList<TouchedProperty> PopulateReference<T>(
        T entity,
        JsonElement docElement,
        IReadOnlyList<INavigation> ownedReferences,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions? serializerOptions,
        IReadOnlyDictionary<string, string>? navigationAliases = null)
    {
        var options = serializerOptions ?? _defaultSerializerOptions;
        var touched = new List<TouchedProperty>();

        foreach (var nav in ownedReferences)
        {
            var fieldName = ResolveRootFieldName(nav, navigationAliases, fieldNamingPolicy);
            if (!docElement.TryGetPropertyCI(fieldName, out var objElement)
                || objElement.ValueKind != JsonValueKind.Object)
                continue;

            // The default relational shaper always instantiates a table-split OwnsOne's CLR
            // object (even when every flat column is null), so this should never be null in
            // practice — but if it somehow is, there's nothing to override in place.
            var existing = nav.PropertyInfo != null
                ? nav.PropertyInfo.GetValue(entity)
                : nav.FieldInfo?.GetValue(entity);
            if (existing == null) continue;

            PopulateReferenceInPlace(existing, objElement, nav.TargetEntityType, fieldNamingPolicy, options, touched);
        }

        return touched;
    }

    /// <summary>
    /// Overrides <paramref name="target"/>'s scalar properties in place from
    /// <paramref name="itemElement"/>, then recurses into any nested owned navigation: further
    /// OwnsOne navigations get the same in-place treatment (they are just as snapshot-tracked as
    /// the top level), while nested OwnsMany navigations reuse the ordinary
    /// create-and-replace materialisation via <see cref="PopulateNestedCollection"/> — collections
    /// aren't part of EF Core's snapshot-based change detection, so there's no tracking-safety
    /// concern to preserve for them the way there is for scalars.
    /// </summary>
    private static void PopulateReferenceInPlace(
        object target,
        JsonElement itemElement,
        IEntityType entityType,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions options,
        List<TouchedProperty> touched)
    {
        PopulateScalarProperties(target, itemElement, entityType, fieldNamingPolicy, options, touched);

        foreach (var ownedNav in entityType.GetNavigations())
        {
            if (!ownedNav.TargetEntityType.IsOwned()) continue;
            var fieldName = fieldNamingPolicy?.ConvertName(ownedNav.Name) ?? ownedNav.Name;
            if (!itemElement.TryGetPropertyCI(fieldName, out var nestedElement)) continue;

            if (ownedNav.IsCollection)
            {
                PopulateNestedCollection(target, nestedElement, ownedNav, fieldNamingPolicy, options);
            }
            else
            {
                if (nestedElement.ValueKind != JsonValueKind.Object) continue;
                var nestedExisting = ownedNav.PropertyInfo != null
                    ? ownedNav.PropertyInfo.GetValue(target)
                    : ownedNav.FieldInfo?.GetValue(target);
                if (nestedExisting == null) continue;

                PopulateReferenceInPlace(nestedExisting, nestedElement, ownedNav.TargetEntityType, fieldNamingPolicy, options, touched);
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

        PopulateScalarProperties(ownedEntity, itemElement, entityType, fieldNamingPolicy, options, touched: null);

        // Nested owned navigations
        foreach (var ownedNav in entityType.GetNavigations())
        {
            if (!ownedNav.TargetEntityType.IsOwned()) continue;
            var fieldName = fieldNamingPolicy?.ConvertName(ownedNav.Name) ?? ownedNav.Name;
            if (!itemElement.TryGetPropertyCI(fieldName, out var nestedElement)) continue;

            if (ownedNav.IsCollection)
            {
                PopulateNestedCollection(ownedEntity, nestedElement, ownedNav, fieldNamingPolicy, options);
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

    /// <summary>
    /// Sets every scalar property found in <paramref name="itemElement"/> onto the existing
    /// <paramref name="target"/> instance (as opposed to <see cref="MaterializeOwnedItem"/>,
    /// which always creates a new instance). Shared by <see cref="MaterializeOwnedItem"/> (via a
    /// brand-new instance) and <see cref="PopulateReferenceInPlace"/> (via an existing, possibly
    /// tracked, instance). When <paramref name="touched"/> is non-null, every property actually
    /// set is recorded into it.
    /// </summary>
    private static void PopulateScalarProperties(
        object target,
        JsonElement itemElement,
        IEntityType entityType,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions options,
        List<TouchedProperty>? touched)
    {
        foreach (var prop in entityType.GetProperties())
        {
            if (prop.IsShadowProperty()) continue;
            var jsonKey = fieldNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
            if (!itemElement.TryGetPropertyCI(jsonKey, out var propElement)) continue;

            var converted = ConvertFromJson(propElement, prop, options);
            // Use the property setter when one exists (public or non-public).
            // Fall back to FieldInfo for backing-field / field-access properties where
            // PropertyInfo is null or has no setter (e.g. init-only / get-only).
            if (prop.PropertyInfo?.GetSetMethod(nonPublic: true) != null)
                prop.PropertyInfo.SetValue(target, converted);
            else if (prop.FieldInfo != null)
                prop.FieldInfo.SetValue(target, converted);
            else
                continue;

            touched?.Add(new TouchedProperty(target, prop));
        }
    }

    /// <summary>
    /// Materialises a nested OwnsMany navigation's array onto <paramref name="owner"/> — the
    /// create-and-replace logic shared by <see cref="MaterializeOwnedItem"/> and
    /// <see cref="PopulateReferenceInPlace"/>. Collections aren't part of EF Core's snapshot-based
    /// change detection (unlike table-split OwnsOne scalars), so replacing the collection wholesale
    /// carries no tracking-safety concern the way overriding a scalar in place does.
    /// </summary>
    private static void PopulateNestedCollection(
        object owner,
        JsonElement nestedElement,
        INavigation ownedNav,
        JsonNamingPolicy? fieldNamingPolicy,
        JsonSerializerOptions options)
    {
        if (nestedElement.ValueKind != JsonValueKind.Array) return;
        var accessor = ownedNav.GetCollectionAccessor();
        // Skip navigations with no accessor and no way to assign — nothing to set.
        if (accessor == null && ownedNav.PropertyInfo == null && ownedNav.FieldInfo == null) return;
        var nestedClrType = ownedNav.TargetEntityType.ClrType;
        if (accessor != null)
        {
            var coll = accessor.GetOrCreate(owner, forMaterialization: true);
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
                ownedNav.PropertyInfo.SetValue(owner, list);
            else if (ownedNav.FieldInfo != null)
                ownedNav.FieldInfo.SetValue(owner, list);
        }
        foreach (var nestedItemElement in nestedElement.EnumerateArray())
        {
            var nestedEntity = MaterializeOwnedItem(nestedItemElement, ownedNav.TargetEntityType, fieldNamingPolicy, options);
            if (accessor != null)
                accessor.Add(owner, nestedEntity, forMaterialization: true);
            else
            {
                // Read back via the same PropertyInfo → FieldInfo fallback.
                var existingList = (IList?)(ownedNav.PropertyInfo != null
                    ? ownedNav.PropertyInfo.GetValue(owner)
                    : ownedNav.FieldInfo?.GetValue(owner));
                existingList?.Add(nestedEntity);
            }
        }
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
    internal static object? ConvertFromJson(JsonElement element, IProperty property, JsonSerializerOptions options)
    {
        var typeMapping = property.FindTypeMapping();
        var converter   = property.GetValueConverter() ?? typeMapping?.Converter;

        // For JSON null: bypass conversion unless the converter has ConvertsNulls=true,
        // in which case it expects to receive null and may return a non-null model value.
        if (element.ValueKind == JsonValueKind.Null)
            return converter is { ConvertsNulls: true }
                ? converter.ConvertFromProvider(null)
                : null;

        // 1. Value converter from HasConversion — checked first because it is the most
        //    common user-facing path and avoids a format mismatch with JsonValueReaderWriter
        //    (e.g. an enum stored as an integer would fail the string reader).
        //    GetValueConverter() delegates to FindTypeMapping()?.Converter in EF Core 10;
        //    we also check FindTypeMapping().Converter directly as a belt-and-suspenders
        //    fallback for owned-entity properties where the two may diverge.
        if (converter != null)
        {
            var providerValue = ConvertJsonValue(element, converter.ProviderClrType, options);
            return converter.ConvertFromProvider(providerValue);
        }

        // 2. Type-mapping JsonValueReaderWriter (e.g. JsonObject, JsonArray, custom mappings
        //    that do not use a ValueConverter but register their own JSON reader).
        var readerWriter = typeMapping?.JsonValueReaderWriter;
        if (readerWriter != null)
        {
            try
            {
                return readerWriter.FromJsonString(element.GetRawText(), existingObject: null);
            }
            catch (FormatException) when (IsIntegerClrType(property.ClrType)
                && element.ValueKind == JsonValueKind.Number)
            {
                // Real-world Couchbase documents sometimes store a whole-number value with a
                // decimal point (e.g. a review rating written as "4.0"), which EF Core's strict
                // built-in int/long JSON readers reject outright. Fall back to a tolerant parse.
                // Restricted to Number tokens so a FormatException from some other invalid shape
                // (e.g. a string or boolean where an int was expected) still surfaces as-is
                // instead of being reinterpreted by ConvertJsonValue.
                return ConvertJsonValue(element, property.ClrType, options);
            }
        }

        // 3. Primitive switch fallback.
        return ConvertJsonValue(element, property.ClrType, options);
    }

    private static bool IsIntegerClrType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(int) || t == typeof(long);
    }

    /// <summary>
    /// Returns <paramref name="value"/> unchanged if it has no fractional component, otherwise
    /// throws — a genuine fraction (e.g. 4.4) must not be silently truncated when parsing an
    /// int/long property.
    /// </summary>
    private static decimal RequireIntegral(decimal value)
    {
        if (decimal.Truncate(value) != value)
            throw new FormatException($"Expected an integral JSON number but got '{value}'.");
        return value;
    }

    /// <summary>
    /// Converts a decimal-formatted whole number (e.g. "4.0") to <see cref="int"/>, throwing
    /// <see cref="FormatException"/> — not <see cref="OverflowException"/> — when out of range,
    /// so an out-of-range value behaves the same whether or not it carries a decimal point (the
    /// integer-formatted case already throws <see cref="FormatException"/> via
    /// <see cref="JsonElement.GetInt32"/>).
    /// </summary>
    private static int ToInt32Checked(decimal value)
    {
        value = RequireIntegral(value);
        if (value < int.MinValue || value > int.MaxValue)
            throw new FormatException($"Value '{value}' is outside the range of Int32.");
        return (int)value;
    }

    /// <summary>Same as <see cref="ToInt32Checked"/>, for <see cref="long"/>.</summary>
    private static long ToInt64Checked(decimal value)
    {
        value = RequireIntegral(value);
        if (value < long.MinValue || value > long.MaxValue)
            throw new FormatException($"Value '{value}' is outside the range of Int64.");
        return (long)value;
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
            // TryGetInt32/64 reject a JSON number with a decimal point (e.g. "4.0") even when it
            // represents a whole value (observed in the Couchbase travel-sample hotel review
            // ratings). Fall back to GetDecimal() — exact for the full int/long range, unlike
            // double — and require the value to actually be integral rather than silently
            // truncating a genuine fraction like 4.4.
            _ when t == typeof(int)      => element.TryGetInt32(out var i32) ? i32 : ToInt32Checked(element.GetDecimal()),
            _ when t == typeof(long)     => element.TryGetInt64(out var i64) ? i64 : ToInt64Checked(element.GetDecimal()),
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
