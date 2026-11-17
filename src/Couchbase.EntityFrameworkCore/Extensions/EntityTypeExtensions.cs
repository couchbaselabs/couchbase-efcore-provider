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
        => BuildLegacyKey(entityType, entity, static (_, clrProperty, entity) => clrProperty.GetValue(entity), entity);

    /// <summary>
    /// The legacy document key of <paramref name="entity"/>: the same string as
    /// <see cref="GetPrimaryKey(IEntityType,object)"/> (the key parts in CLR property order), but
    /// with the values read from <paramref name="updateEntry"/>.
    /// </summary>
    /// <remarks>
    /// During a save, a store-generated key value (such as a sequence value, or a foreign key that
    /// EF Core set from one) is in the update entry only: EF Core copies it to the CLR object when
    /// it accepts the changes, after the documents are written.
    /// </remarks>
    internal static string GetLegacyPrimaryKey(
        this IEntityType entityType, object entity, Microsoft.EntityFrameworkCore.Update.IUpdateEntry updateEntry)
        => BuildLegacyKey(entityType, entity, static (key, _, entry) => entry.GetCurrentValue(key), updateEntry);

    /// <summary>
    /// Joins the key parts with <c>'_'</c>, ordered by the entity's CLR properties in reverse
    /// declaration order (the legacy format), reading each value with <paramref name="readValue"/>.
    /// </summary>
    private static string BuildLegacyKey<TState>(
        IEntityType entityType,
        object entity,
        Func<IProperty, System.Reflection.PropertyInfo, TState, object?> readValue,
        TState state)
    {
        var keys = (entityType.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityType.DisplayName()}' has no primary key defined."))
            .Properties;

        var compositeKey = new StringBuilder();
        foreach (var property in entity.GetType().GetProperties().Reverse())
        {
            foreach (var key in keys)
            {
                if (key.Name != property.Name) continue;
                if (compositeKey.Length > 0)
                {
                    compositeKey.Append('_'); //TODO delimiter should be optional and customizable
                }

                compositeKey.Append(FormatKeyPart(readValue(key, property, state)));
            }
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
