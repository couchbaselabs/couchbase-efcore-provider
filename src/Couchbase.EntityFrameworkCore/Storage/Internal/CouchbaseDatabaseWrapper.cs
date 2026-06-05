using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Internal;
using Couchbase.EntityFrameworkCore.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper : Database
{
    private readonly ICouchbaseClientWrapper _couchbaseClient;
    private readonly IRelationalConnection _relationalConnection;
    private readonly JsonNamingPolicy? _fieldNamingPolicy;

    public CouchbaseDatabaseWrapper(
        DatabaseDependencies dependencies,
        ICouchbaseClientWrapper couchbaseClient,
        IRelationalConnection relationalConnection,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
        : base(dependencies)
    {
        _couchbaseClient = couchbaseClient ?? throw new ArgumentNullException(nameof(couchbaseClient));
        _relationalConnection = relationalConnection ?? throw new ArgumentNullException(nameof(relationalConnection));
        _fieldNamingPolicy = couchbaseDbContextOptionsBuilder.FieldNamingPolicy;
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
#if DEBUG
        return AsyncHelper.RunSync(
            static state => state.self.SaveChangesAsync(state.entries),
            (self: this, entries));
#else
        throw ExceptionHelper.SyncroIONotSupportedException();
#endif
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new())
    {
        var transaction = GetCurrentTransaction();
        var updateCount = 0;

        // Track root-entity keys written in the main loop so the second pass can skip
        // owners that were already written (e.g. user manually set entry.State = Modified).
        var writtenRoots = new HashSet<string>();
        // Owned entries whose owner must be written in the second pass.
        var deferredOwnedEntries = new List<IUpdateEntry>();

        foreach (var updateEntry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;

            if (entityType.IsOwned())
            {
                // Collect owned entries with actual changes; their owners will be written
                // in the second pass below if not already written by the main loop.
                if (updateEntry.EntityState is not (EntityState.Unchanged or EntityState.Detached))
                    deferredOwnedEntries.Add(updateEntry);
                continue;
            }

            // Shared entity types (e.g. hidden join table from HasMany().WithMany()) have
            // all PK columns as shadow properties — use the IUpdateEntry overload so the
            // values are read from the entry rather than the CLR Dictionary<string,object>.
            var primaryKey = entityType.HasSharedClrType
                ? entityType.GetPrimaryKey(updateEntry)
                : entityType.GetPrimaryKey(entity);
            var keyspace = entityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                case EntityState.Unchanged:
                    // Not written — intentionally NOT added to writtenRoots so the deferred-owned
                    // pass can still write the owner if an owned item triggered a change.
                    break;

                case EntityState.Deleted:
                    // Add to writtenRoots so the deferred pass doesn't upsert a just-deleted doc.
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    if (transaction != null)
                    {
                        await _couchbaseClient.EnqueueTransactionalRemove(transaction, primaryKey, keyspace).ConfigureAwait(false);
                        updateCount++;
                    }
                    else
                    {
                        if (await _couchbaseClient.DeleteDocument(primaryKey, keyspace).ConfigureAwait(false))
                        {
                            updateCount++;
                        }
                    }
                    break;

                case EntityState.Modified:
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    var modifiedDocument = HydrateObjectFromEntity(updateEntry, _fieldNamingPolicy);
                    if (transaction != null)
                    {
                        await _couchbaseClient.EnqueueTransactionalUpsert(transaction, primaryKey, keyspace, modifiedDocument).ConfigureAwait(false);
                        updateCount++;
                    }
                    else
                    {
                        if (await _couchbaseClient.UpdateDocument(primaryKey, keyspace, modifiedDocument).ConfigureAwait(false))
                        {
                            updateCount++;
                        }
                    }
                    break;

                case EntityState.Added:
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    var newDocument = HydrateObjectFromEntity(updateEntry, _fieldNamingPolicy);
                    if (transaction != null)
                    {
                        await _couchbaseClient.EnqueueTransactionalInsert(transaction, primaryKey, keyspace, newDocument).ConfigureAwait(false);
                        updateCount++;
                    }
                    else
                    {
                        if (await _couchbaseClient.CreateDocument(primaryKey, keyspace, newDocument).ConfigureAwait(false))
                        {
                            updateCount++;
                        }
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Second pass: for each owned entry with a state change, write the owner document
        // if it was not already written in the main loop above. This removes the need for
        // callers to manually set ctx.Entry(owner).State = EntityState.Modified after
        // mutating owned navigations (OwnsOne property changes, OwnsMany collection mutation).
        foreach (var ownedEntry in deferredOwnedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Walk the ownership chain up to the root non-owned entity.
            // Deeply nested owned types (e.g. ContactTag → ContactMethod → Customer) require
            // multiple hops; stop at the first principal whose entity type is not owned —
            // only non-owned types have an independent Couchbase keyspace to write to.
            var currentEntry = (InternalEntityEntry)ownedEntry;
            InternalEntityEntry? rootInternalEntry = null;
            IEntityType? rootEntityType = null;

            while (true)
            {
                var ownership = currentEntry.EntityType.FindOwnership();
                if (ownership == null) break;

                var fkValues = ownership.Properties
                    .Select(p => currentEntry.GetCurrentValue(p))
                    .ToArray();

                var ownerEntry = currentEntry.StateManager.TryGetEntry(ownership.PrincipalKey, fkValues);
                if (ownerEntry == null) break;

                if (!ownership.PrincipalEntityType.IsOwned())
                {
                    rootInternalEntry = ownerEntry;
                    rootEntityType = ownership.PrincipalEntityType;
                    break;
                }

                currentEntry = ownerEntry;
            }

            if (rootInternalEntry == null || rootEntityType == null) continue;

            var ownerEntity = rootInternalEntry.Entity;
            var ownerPrimaryKey = rootEntityType.GetPrimaryKey(ownerEntity);
            var rootKey = $"{rootEntityType.ClrType.Name}:{ownerPrimaryKey}";

            if (writtenRoots.Contains(rootKey)) continue;
            writtenRoots.Add(rootKey);

            var ownerKeyspace = rootEntityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Owner entity type '{rootEntityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            var ownerDocument = HydrateObjectFromEntity(rootInternalEntry, _fieldNamingPolicy);

            if (transaction != null)
            {
                await _couchbaseClient.EnqueueTransactionalUpsert(transaction, ownerPrimaryKey, ownerKeyspace, ownerDocument).ConfigureAwait(false);
                updateCount++;
            }
            else
            {
                if (await _couchbaseClient.UpdateDocument(ownerPrimaryKey, ownerKeyspace, ownerDocument).ConfigureAwait(false))
                    updateCount++;
            }
        }

        return updateCount;
    }

    // Internal seam so the null-nav behaviour can be verified without a live DbContext.
    internal static void FillOwnsOneIntoDoc(Dictionary<string, object?> doc, INavigation nav, object? navValue)
    {
        foreach (var p in nav.TargetEntityType.GetProperties().Where(p => !p.IsShadowProperty()))
        {
            // Read via PropertyInfo when available; fall back to FieldInfo for field-access properties.
            var value = navValue == null ? null
                : p.PropertyInfo != null ? p.PropertyInfo.GetValue(navValue)
                : p.FieldInfo?.GetValue(navValue);
            doc[p.GetColumnName()] = value;
        }
    }

    private CouchbaseDbTransaction? GetCurrentTransaction()
    {
        if (_relationalConnection is CouchbaseRelationalConnection couchbaseRelationalConnection)
        {
            return couchbaseRelationalConnection.CouchbaseTransaction;
        }
        return null;
    }

    internal static object HydrateObjectFromEntity(IUpdateEntry updateEntry, JsonNamingPolicy? fieldNamingPolicy = null)
    {
        var entityType = updateEntry.EntityType;

        var ownedNavs = entityType.GetNavigations()
            .Where(n => n.TargetEntityType.IsOwned() && n.PropertyInfo != null)
            .ToList();

        // No owned navigations: use CLR instance so the SDK's camelCase serializer produces
        // the same field names that already work for all existing entity types.
        //
        // Exception: shared entity types (HasSharedClrType == true, e.g. the hidden join
        // entity for skip navigations / HasMany().WithMany()) use Dictionary<string,object>
        // as their CLR type.  All their FK properties are shadow properties — the standard
        // IsShadowProperty filter would produce an empty document.  Additionally, Dictionary's
        // indexer throws TargetParameterCountException when PropertyInfo.SetValue is called
        // without index arguments.  For shared types we therefore build a plain dictionary
        // keyed by column name from ALL (including shadow) properties.
        if (ownedNavs.Count == 0)
        {
            if (entityType.HasSharedClrType)
            {
                // Shared entity type (skip-navigation join table): all FK columns are shadow
                // properties — write every property value by column name.
                var joinDoc = new Dictionary<string, object?>();
                foreach (var property in entityType.GetProperties())
                {
                    // Use GetColumnName() verbatim — do NOT apply fieldNamingPolicy here.
                    // The SQL generation path projects these columns by their exact column
                    // name (e.g. "PostsPostId", "TagsTagId"), so the written document keys
                    // must match. Applying a naming policy would silently diverge from the
                    // SQL projection and make join documents unreadable on the query path.
                    joinDoc[property.GetColumnName()] = updateEntry.GetCurrentValue(property);
                }
                return joinDoc;
            }

            var obj = Activator.CreateInstance(entityType.ClrType)!;
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsShadowProperty()) continue;
                var value = updateEntry.GetCurrentValue(property);
                if (property.PropertyInfo?.GetSetMethod(nonPublic: true) != null)
                    property.PropertyInfo.SetValue(obj, value);
                else if (property.FieldInfo != null)
                    property.FieldInfo.SetValue(obj, value);
            }
            return obj;
        }

        // With owned navigations, build a flat dictionary so that:
        //   OwnsOne  → each owned scalar is stored as a top-level field whose key matches
        //              EF Core's projected column name (e.g. "address_street"), allowing the
        //              EF Core shaper to read it directly from the N1QL result row.
        //   OwnsMany → the collection is stored under its camelCase navigation name
        //              (e.g. "contactMethods") so AddOwnedCollectionColumnsToProjection
        //              (CouchbaseShapedQueryCompilingExpressionVisitor) can inject a
        //              ColumnExpression into the IR projection, and PopulateCollectionNavigations
        //              can look up the same key in the N1QL result row.
        // Dictionary keys are serialized verbatim by System.Text.Json — no camelCase
        // transformation — so GetColumnName() keys match what N1QL SELECT returns.
        //
        // Known limitations (apply equally to the CLR-instance path above):
        //   1. EF Core value converters (HasConversion) are not applied. GetCurrentValue
        //      returns the model-side CLR value; property.GetValueConverter()?.ConvertToProvider
        //      is never called. This affects all entity writes and should be fixed in a
        //      dedicated write-path correctness pass.
        //   2. Properties with no PropertyInfo and no FieldInfo are silently dropped.
        //
        // Additional limitation specific to the OwnsMany item dictionary (built below):
        //   3. A custom JsonConverter<TOwnedItem> registered in the SDK's serializer options
        //      will not fire for owned-item dictionaries, because the item-type boundary is
        //      erased. The SDK sees Dictionary<string,object?> rather than TOwnedItem and
        //      dispatches on the runtime type of each property value instead.
        var doc = new Dictionary<string, object?>();
        foreach (var property in entityType.GetProperties())
        {
            if (property.IsShadowProperty()) continue;
            doc[property.GetColumnName()] = updateEntry.GetCurrentValue(property);
        }

        var entity = updateEntry.ToEntityEntry().Entity;
        foreach (var nav in ownedNavs)
        {
            // Read via PropertyInfo when available; fall back to FieldInfo for field-access
            // properties (backing-field or [BackingField]-annotated) where PropertyInfo is null.
            var navValue = nav.PropertyInfo != null
                ? nav.PropertyInfo.GetValue(entity)
                : nav.FieldInfo?.GetValue(entity);
            if (nav.IsCollection)
            {
                var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;
                if (navValue is IEnumerable items)
                {
                    // Use SerializeOwnedItem so that nested OwnsOne / OwnsMany navigations
                    // within each item (e.g. ContactMethod.Label, ContactMethod.Tags) are
                    // recursively included.  The flat scalar-only loop it replaces silently
                    // dropped all nested navigations.
                    var list = new List<Dictionary<string, object?>>();
                    foreach (var item in items)
                        list.Add(SerializeOwnedItem(item, nav.TargetEntityType, fieldNamingPolicy));
                    doc[fieldName] = list;
                }
                else
                {
                    doc[fieldName] = null;
                }
            }
            else
            {
                FillOwnsOneIntoDoc(doc, nav, navValue);
            }
        }

        return doc;
    }

    /// <summary>
    /// Recursively serializes a single owned-item CLR instance to a dictionary,
    /// including any nested OwnsOne / OwnsMany navigations at arbitrary depth.
    /// </summary>
    private static Dictionary<string, object?> SerializeOwnedItem(
        object item,
        IEntityType entityType,
        JsonNamingPolicy? fieldNamingPolicy)
    {
        var doc = new Dictionary<string, object?>();

        foreach (var p in entityType.GetProperties())
        {
            if (p.IsShadowProperty()) continue;
            // Read via PropertyInfo → FieldInfo fallback (field-access support).
            var rawValue = p.PropertyInfo != null
                ? p.PropertyInfo.GetValue(item)
                : p.FieldInfo?.GetValue(item);
            // Apply value converter (HasConversion) so the stored value is the
            // provider/storage representation, not the raw CLR model value.
            // GetValueConverter() delegates to FindTypeMapping()?.Converter in EF Core 10;
            // check FindTypeMapping().Converter directly as a fallback for owned-entity
            // properties where the two may diverge.
            var converter   = p.GetValueConverter() ?? p.FindTypeMapping()?.Converter;
            // Apply the converter when present — even if ConvertToProvider returns null
            // (a valid scenario for converters that intentionally map some values to null).
            // Only bypass the converter when none is configured.
            var storedValue = rawValue is null ? null
                : converter is not null ? converter.ConvertToProvider(rawValue) : rawValue;
            doc[fieldNamingPolicy?.ConvertName(p.Name) ?? p.Name] = storedValue;
        }

        foreach (var nav in entityType.GetNavigations())
        {
            if (!nav.TargetEntityType.IsOwned() || nav.PropertyInfo == null) continue;
            var navValue = nav.PropertyInfo.GetValue(item);
            var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;

            if (nav.IsCollection)
            {
                if (navValue is IEnumerable nested)
                {
                    var list = new List<Dictionary<string, object?>>();
                    foreach (var nestedItem in nested)
                        list.Add(SerializeOwnedItem(nestedItem, nav.TargetEntityType, fieldNamingPolicy));
                    doc[fieldName] = list;
                }
                else
                {
                    doc[fieldName] = null;
                }
            }
            else
            {
                if (navValue == null)
                {
                    doc[fieldName] = null;
                }
                else
                {
                    doc[fieldName] = SerializeOwnedItem(navValue, nav.TargetEntityType, fieldNamingPolicy);
                }
            }
        }

        return doc;
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
