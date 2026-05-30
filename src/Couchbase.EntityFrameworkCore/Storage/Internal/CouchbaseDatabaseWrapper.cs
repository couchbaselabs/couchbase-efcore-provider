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

            var primaryKey = entityType.GetPrimaryKey(entity);
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

            var ownership = ownedEntry.EntityType.FindOwnership();
            if (ownership == null) continue;

            // FK values on the owned entry are equal to the owner's PK values.
            var fkValues = ownership.Properties
                .Select(p => ownedEntry.GetCurrentValue(p))
                .ToArray();

            var ownerEntityType = ownership.PrincipalEntityType;

            // Use StateManager.TryGetEntry for O(1) owner lookup instead of a linear
            // ChangeTracker.Entries() scan, and get the InternalEntityEntry directly so
            // the IInfrastructure<InternalEntityEntry> unwrap is no longer needed.
            var stateManager = ((InternalEntityEntry)ownedEntry).StateManager;
            var ownerInternalEntry = stateManager.TryGetEntry(ownership.PrincipalKey, fkValues);
            if (ownerInternalEntry == null) continue;

            var ownerEntity = ownerInternalEntry.Entity;
            var ownerPrimaryKey = ownerEntityType.GetPrimaryKey(ownerEntity);
            var rootKey = $"{ownerEntityType.ClrType.Name}:{ownerPrimaryKey}";

            if (writtenRoots.Contains(rootKey)) continue;
            writtenRoots.Add(rootKey);

            var ownerKeyspace = ownerEntityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Owner entity type '{ownerEntityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            var ownerDocument = HydrateObjectFromEntity(ownerInternalEntry, _fieldNamingPolicy);

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
            doc[p.GetColumnName()] = navValue == null ? null : p.PropertyInfo?.GetValue(navValue);
    }

    private CouchbaseDbTransaction? GetCurrentTransaction()
    {
        if (_relationalConnection is CouchbaseRelationalConnection couchbaseRelationalConnection)
        {
            return couchbaseRelationalConnection.CouchbaseTransaction;
        }
        return null;
    }

    private static object HydrateObjectFromEntity(IUpdateEntry updateEntry, JsonNamingPolicy? fieldNamingPolicy = null)
    {
        var entityType = updateEntry.EntityType;

        var ownedNavs = entityType.GetNavigations()
            .Where(n => n.TargetEntityType.IsOwned() && n.PropertyInfo != null)
            .ToList();

        // No owned navigations: use CLR instance so the SDK's camelCase serializer produces
        // the same field names that already work for all existing entity types.
        if (ownedNavs.Count == 0)
        {
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
            var navValue = nav.PropertyInfo!.GetValue(entity);
            if (nav.IsCollection)
            {
                var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;
                if (navValue is IEnumerable items)
                {
                    var itemProps = nav.TargetEntityType.GetProperties()
                        .Where(p => !p.IsShadowProperty()).ToList();
                    var list = new List<Dictionary<string, object?>>();
                    foreach (var item in items)
                    {
                        var itemDoc = new Dictionary<string, object?>();
                        foreach (var p in itemProps)
                            itemDoc[fieldNamingPolicy?.ConvertName(p.Name) ?? p.Name] = p.PropertyInfo?.GetValue(item);
                        list.Add(itemDoc);
                    }
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
