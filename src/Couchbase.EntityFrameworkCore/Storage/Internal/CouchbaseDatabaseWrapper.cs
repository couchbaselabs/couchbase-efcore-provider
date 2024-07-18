using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper(DatabaseDependencies dependencies, ICouchbaseClientWrapper couchbaseClient)
    : Database(dependencies)
{
    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
       return Task.Run(async () => await SaveChangesAsync(entries).ConfigureAwait(false)).Result;
    }
    
    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new CancellationToken())
    {
        var updateCount = 0;
        foreach (var updateEntry in entries)
        {
            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;
            var primaryKey = GetPrimaryKey(entity, entityType);
            var keyspace = entityType.GetTableName();
            
            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                case EntityState.Deleted:
                    if (await couchbaseClient.DeleteDocument(primaryKey, keyspace, entity).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Modified:
                    if (await couchbaseClient.UpdateDocument(primaryKey, keyspace, entity).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Added:
                    if (await couchbaseClient.CreateDocument(primaryKey, keyspace, entity).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return updateCount;
    }

    private static string GetPrimaryKey(object entity, IEntityType entityType)
    {
        var keys = entityType.FindPrimaryKey().Properties.ToArray(); //TODO If no primary key found we should fail hard

        var compositeKey = new StringBuilder();
        foreach (var property in entity.GetType().GetProperties().Reverse())
        {
            foreach (var key in keys)
            {
                if (key.Name != property.Name) continue;
                if (compositeKey.Length > 0)
                {
                    compositeKey.Append("_"); //TODO delimiter should be optional and customizable
                }
                var keyValue = property.GetValue(entity);
                compositeKey.Append(keyValue);
            }
        }
        return compositeKey.ToString();
    }
}