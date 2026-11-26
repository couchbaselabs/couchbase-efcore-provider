using System.Text;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;

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
    /// Formats one key value. A <c>byte[]</c> is written as Base64, and an empty array as <c>"~"</c>,
    /// which Base64 never gives. Other values use <see cref="object.ToString"/>.
    /// </summary>
    private static string? FormatKeyPart(object? value)
        => value is byte[] bytes
            ? bytes.Length == 0 ? "~" : Convert.ToBase64String(bytes)
            : value?.ToString();

    /// <summary>
    /// Returns the <see cref="CouchbaseDocumentKeyFormat.Legacy"/> document key of
    /// <paramref name="entity"/>, with the values read from the entity object.
    /// </summary>
    [Obsolete("This returns the Legacy-format document key. It matches the stored document id only when the " +
              "context uses UseDocumentKeyFormat(CouchbaseDocumentKeyFormat.Legacy). V2 is the default.")]
    public static string GetPrimaryKey(this IEntityType entityType, object entity)
        => BuildLegacyKey(entityType, entity,
            static (key, entity) => key.GetGetter().GetClrValueUsingContainingEntity(entity), entity);

    /// <summary>
    /// Returns the <see cref="CouchbaseDocumentKeyFormat.Legacy"/> document key of
    /// <paramref name="entity"/>, with the values read from <paramref name="updateEntry"/>.
    /// </summary>
    /// <remarks>
    /// During a save, a store-generated key value is only in the update entry. EF Core copies it
    /// to the entity object after the documents are written.
    /// </remarks>
    internal static string GetLegacyPrimaryKey(this IEntityType entityType, object entity, IUpdateEntry updateEntry)
        => BuildLegacyKey(entityType, entity, static (key, entry) => entry.GetCurrentValue(key), updateEntry);

    /// <summary>
    /// Joins the key parts with <c>'_'</c>, reading each value with <paramref name="readValue"/>.
    /// </summary>
    /// <remarks>
    /// The parts follow the reverse declaration order of the entity's public CLR properties. A shared-type
    /// entity (such as a many-to-many join entity) has no such properties, so its parts follow the
    /// declared primary key order.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A key part of a non-shared entity type is not a public property of the entity's CLR type.
    /// </exception>
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

        IEnumerable<IProperty> orderedKeys;
        if (entityType.HasSharedClrType)
        {
            orderedKeys = keys;
        }
        else
        {
            var publicProperties = entity.GetType().GetProperties();
            foreach (var key in keys)
            {
                if (key.IsShadowProperty() || key.PropertyInfo is null
                    || !publicProperties.Any(property => property.Name == key.Name))
                {
                    throw new InvalidOperationException(
                        $"The key property '{key.Name}' of entity type '{entityType.DisplayName()}' is not a public " +
                        "property. The Legacy document key format supports only key parts that are public " +
                        "properties. Use the V2 format, which is the default: remove the call to " +
                        "UseDocumentKeyFormat(CouchbaseDocumentKeyFormat.Legacy).");
                }
            }

            orderedKeys = publicProperties.Reverse()
                .SelectMany(property => keys.Where(key => key.Name == property.Name));
        }

        var compositeKey = new StringBuilder();
        foreach (var key in orderedKeys)
        {
            if (compositeKey.Length > 0)
            {
                compositeKey.Append('_');
            }

            compositeKey.Append(FormatKeyPart(readValue(key, state)));
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
