using System.Collections.Concurrent;
using System.Text.Json;
using Couchbase.Core.IO.Serializers;
using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.VisualBasic;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper(DatabaseDependencies dependencies, ICouchbaseClientWrapper couchbaseClient)
    : Database(dependencies)
{
    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
#if DEBUG
        //Required for test infrastructure database creation and seeding
        return SaveChangesAsync(entries).ConfigureAwait(false).GetAwaiter().GetResult();
#else
        ExceptionHelper.SyncroIONotSupportedException();
#endif
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new())
    {
        var updateCount = 0;
        foreach (var updateEntry in entries)
        {
            // entity info
            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;

            // document info
            var primaryKey = entityType.GetPrimaryKey(entity);

            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                case EntityState.Deleted:
                    if (await couchbaseClient.DeleteDocument(primaryKey, entityType.GetCollectionName()).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Modified:
                    var modifiedDocument = HydrateObjectFromEntity(updateEntry);
                    if (await couchbaseClient.UpdateDocument(primaryKey,  entityType.GetCollectionName(), modifiedDocument).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Added:
                {
                    var newDocument = HydrateObjectFromEntity(updateEntry);
                    if (await couchbaseClient.CreateDocument(primaryKey,  entityType.GetCollectionName(), newDocument).ConfigureAwait(false))
                    {
                        updateCount++;
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return updateCount;
    }

    private readonly ITypeTranscoder _transcoder = new JsonTranscoder(new DefaultSerializer());

    private static object HydrateObjectFromEntity(IUpdateEntry updateEntry)
    {
        var entityType = updateEntry.EntityType;
        var type = entityType.ClrType;
        var obj = Activator.CreateInstance(type);

        foreach (var property in entityType.GetProperties())
        {
            //Shadow properties are properties that aren't defined in your .NET entity
            //class but are defined for that entity type in the EF Core model. The
            //value and state of these properties are maintained purely in the Change
            //Tracker. Shadow properties are useful when there's data in the database
            //that shouldn't be exposed on the mapped entity types.
            //https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties
            if (!property.IsShadowProperty())
            {
                var propertyInfo = type.GetProperty(property.Name);
                propertyInfo.SetValue(
                    obj, updateEntry.GetCurrentValue(property));
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
