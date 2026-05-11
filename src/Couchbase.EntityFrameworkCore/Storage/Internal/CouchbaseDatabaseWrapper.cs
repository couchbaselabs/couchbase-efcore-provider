using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper : Database
{
    private readonly ICouchbaseClientWrapper _couchbaseClient;
    private readonly IRelationalConnection _relationalConnection;

    public CouchbaseDatabaseWrapper(
        DatabaseDependencies dependencies,
        ICouchbaseClientWrapper couchbaseClient,
        IRelationalConnection relationalConnection)
        : base(dependencies)
    {
        _couchbaseClient = couchbaseClient ?? throw new ArgumentNullException(nameof(couchbaseClient));
        _relationalConnection = relationalConnection ?? throw new ArgumentNullException(nameof(relationalConnection));
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
            var keyspace = entityType.GetCollectionName();

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
                    var modifiedDocument = HydrateObjectFromEntity(updateEntry);
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
                    var newDocument = HydrateObjectFromEntity(updateEntry);
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

    private CouchbaseDbTransaction? GetCurrentTransaction()
    {
        if (_relationalConnection is CouchbaseRelationalConnection couchbaseRelationalConnection)
        {
            return couchbaseRelationalConnection.CouchbaseTransaction;
        }
        return null;
    }

    private static object HydrateObjectFromEntity(IUpdateEntry updateEntry)
    {
        var entityType = updateEntry.EntityType;
        var type = entityType.ClrType;
        var obj = Activator.CreateInstance(type);

        foreach (var property in entityType.GetProperties())
        {
            if (!property.IsShadowProperty())
            {
                var propertyInfo = type.GetProperty(property.Name);
                propertyInfo.SetValue(obj, updateEntry.GetCurrentValue(property));
            }
        }

        return obj;
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
