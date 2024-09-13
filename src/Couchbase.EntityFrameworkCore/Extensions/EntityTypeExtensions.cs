using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class EntityTypeExtensions
{
    public static string GetScopeAndCollection(this IEntityType entityType)
    {
        return entityType.GetTableName();
    }
    
    public static string GetPrimaryKey(this IEntityType entityType, object entity)
    {
        var keys = entityType.FindPrimaryKey().Properties
            .ToArray(); //TODO If no primary key found we should fail hard

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