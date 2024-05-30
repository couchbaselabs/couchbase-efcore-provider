using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper(DatabaseDependencies dependencies, ICouchbaseClientWrapper couchbaseClient)
    : Database(dependencies)
{
    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
        throw new NotImplementedException();
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
           
            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                case EntityState.Deleted:
                    await couchbaseClient.DeleteDocument(primaryKey).ConfigureAwait(false);
                    break;
                case EntityState.Modified:
                    await couchbaseClient.UpdateDocument(primaryKey, entity).ConfigureAwait(false);
                    break;
                case EntityState.Added:
                    await couchbaseClient.CreateDocument(primaryKey, entity).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            updateCount++;
        }

        return updateCount;
    }

    private string GetPrimaryKey(object entity, IEntityType entityType)
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