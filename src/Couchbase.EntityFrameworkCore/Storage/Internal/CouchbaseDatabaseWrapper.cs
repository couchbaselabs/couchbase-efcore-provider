using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Utils;
using Microsoft.EntityFrameworkCore;
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
        return SaveChangesAsync(entries).ConfigureAwait(false).GetAwaiter().GetResult();
#else
        throw ExceptionHelper.SyncroIONotSupportedException();
#endif
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new())
    {
        var transaction = GetCurrentTransaction();
        var updateCount = 0;

        foreach (var updateEntry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;

            if (entityType.IsOwned())
            {
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
                    break;

                case EntityState.Deleted:
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
