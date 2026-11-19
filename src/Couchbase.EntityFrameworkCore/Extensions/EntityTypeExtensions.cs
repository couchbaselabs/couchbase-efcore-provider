using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class EntityTypeExtensions
{
    public static string GetCollectionName(this IEntityType entityType)
    {
        return entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Entity type '{entityType.DisplayName()}' is not mapped to a collection.");
    }

    /// <summary>
    /// Formats one key value for the document key. A <c>byte[]</c> value is written as Base64:
    /// formatting it like any other object gives the text <c>"System.Byte[]"</c> for every instance,
    /// so every entity keyed by a byte array shared one document and silently overwrote the others.
    /// An empty array would be an empty string (an invalid document ID), so it is written as
    /// <c>"~"</c>, which can never be produced by Base64. Every other type is formatted exactly as before.
    /// </summary>
    private static string? FormatKeyPart(object? value)
        => value is byte[] bytes
            ? bytes.Length == 0 ? "~" : Convert.ToBase64String(bytes)
            : value?.ToString();

    public static string GetPrimaryKey(this IEntityType entityType, object entity)
        => BuildLegacyKey(entityType, entity,
            static (key, entity) => key.IsShadowProperty()
                ? throw new InvalidOperationException(
                    $"The key property '{key.DeclaringType.DisplayName()}.{key.Name}' is a shadow property, so its " +
                    "value cannot be read from the entity object. Build the key from the change tracker entry instead.")
                : key.GetGetter().GetClrValueUsingContainingEntity(entity),
            entity);

    /// <summary>
    /// The legacy document key of <paramref name="entity"/>: the same string as
    /// <see cref="GetPrimaryKey(IEntityType,object)"/>, but with the values read from
    /// <paramref name="updateEntry"/>.
    /// </summary>
    /// <remarks>
    /// During a save, a store-generated key value (such as a sequence value, or a foreign key that
    /// EF Core set from one) is in the update entry only: EF Core copies it to the CLR object when
    /// it accepts the changes, after the documents are written. The update entry also has the
    /// values of shadow key properties, which the CLR object does not.
    /// </remarks>
    internal static string GetLegacyPrimaryKey(
        this IEntityType entityType, object entity, Microsoft.EntityFrameworkCore.Update.IUpdateEntry updateEntry)
        => BuildLegacyKey(entityType, entity, static (key, entry) => entry.GetCurrentValue(key), updateEntry);

    /// <summary>
    /// Joins the key parts with <c>'_'</c>, reading each value with <paramref name="readValue"/>.
    /// </summary>
    /// <remarks>
    /// The legacy format orders the parts by the entity's public CLR properties in reverse
    /// declaration order. Existing documents use that order, so it is kept whenever every key part
    /// is a public CLR property. A key part can also be a shadow property, a mapped field or a
    /// non-public property. The legacy format skipped such parts, which gave an empty key (the
    /// insert failed) or a composite key with parts missing (different entities shared one
    /// document). Such a key uses the primary key's declared order instead.
    /// </remarks>
    private static string BuildLegacyKey<TState>(
        IEntityType entityType,
        object entity,
        Func<IProperty, TState, object?> readValue,
        TState state)
    {
        var keys = (entityType.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityType.DisplayName()}' has no primary key defined."))
            .Properties;

        var publicProperties = entity.GetType().GetProperties();
        var orderedKeys = keys.All(key => publicProperties.Any(property => property.Name == key.Name))
            ? publicProperties.Reverse()
                .SelectMany(property => keys.Where(key => key.Name == property.Name))
            : keys;

        var compositeKey = new StringBuilder();
        foreach (var key in orderedKeys)
        {
            if (compositeKey.Length > 0)
            {
                compositeKey.Append('_'); //TODO delimiter should be optional and customizable
            }

            compositeKey.Append(FormatKeyPart(readValue(key, state)));
        }

        if (compositeKey.Length == 0)
        {
            throw new InvalidOperationException(
                $"The document key for entity type '{entityType.DisplayName()}' is empty, and Couchbase does not " +
                "allow an empty document id. Give the key property a non-empty value.");
        }

        return compositeKey.ToString();
    }

    /// <summary>
    /// Returns the document key for <paramref name="updateEntry"/>, reading PK values
    /// from the update entry directly.  This handles shared entity types (e.g. the hidden
    /// join table created by <c>HasMany().WithMany()</c>) whose PK columns are shadow
    /// properties — <see cref="GetPrimaryKey(IEntityType,object)"/> cannot read these via
    /// CLR <c>PropertyInfo</c>.
    /// </summary>
    public static string GetPrimaryKey(this IEntityType entityType, Microsoft.EntityFrameworkCore.Update.IUpdateEntry updateEntry)
    {
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{entityType.DisplayName()}' has no primary key defined. " +
                "Keyless entity types cannot be used with GetPrimaryKey.");

        var compositeKey = new StringBuilder();
        foreach (var key in primaryKey.Properties)
        {
            if (compositeKey.Length > 0)
                compositeKey.Append('_');
            compositeKey.Append(FormatKeyPart(updateEntry.GetCurrentValue(key)));
        }
        return compositeKey.ToString();
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
