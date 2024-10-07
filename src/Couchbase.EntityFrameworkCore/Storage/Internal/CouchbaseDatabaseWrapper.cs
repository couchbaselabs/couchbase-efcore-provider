using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
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
    
    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new())
    {
        var updateCount = 0;
        foreach (var updateEntry in entries)
        {
            //entity info
            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;

            //document info
            var primaryKey = entityType.GetPrimaryKey(entity);
            var collectionName = entityType.GetCollectionName();
            var document= GenerateRootJson(updateEntry);

            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                case EntityState.Deleted:
                    if (await couchbaseClient.DeleteDocument(primaryKey, collectionName).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Modified:
                    if (await couchbaseClient.UpdateDocument(primaryKey, collectionName, document).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Added:
                {
                    if (await couchbaseClient.CreateDocument(primaryKey, collectionName, document).ConfigureAwait(false))
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
    
    
    private byte[] GenerateRootJson(IUpdateEntry updateEntry)
    {
        try
        {
            var entityType = updateEntry.EntityType;
            JsonWriterOptions writerOptions = new() { Indented = true };

            using MemoryStream stream = new();
            using Utf8JsonWriter writer = new(stream, writerOptions);
            writer.WriteStartObject();

            foreach (var property in entityType.GetProperties())
            {
                var jsonPropertyName = property.FindAnnotation("Relational:JsonPropertyName");
                var fieldName = jsonPropertyName?.Value?.ToString() ?? property.Name;
                var value = updateEntry.GetCurrentValue(property);
                var propertyType = GetUnderlyingType(property.ClrType);
                switch (propertyType.Name)
                {
                    case "String":
                        writer.WriteString(fieldName, (string)value);
                        break;
                    case "Int32":
                        writer.WriteNumber(fieldName, (int)value);
                        break;
                    case "DateTime":
                        writer.WriteString(fieldName, (DateTime)value);
                        break;
                    case "Decimal":
                        writer.WriteNumber(fieldName, (decimal)value);
                        break;
                    case "Byte[]":
                        writer.WriteBase64String(property.Name, new ReadOnlyMemory<byte>((byte[])value).Span);
                        break;
                    default:
                    {
                        if (propertyType.IsEnum)
                        {
                            writer.WriteString(property.Name, value != null ? value.ToString() : string.Empty);
                        }
                        else
                        {
                            throw new JsonException();
                        }
                        break;
                    }
                }
            }

            writer.WriteEndObject();
            writer.Flush();
            return stream.ToArray();
        }
        catch (Exception e)
        {
            
        }

        return null;
    }

    private Type? GetUnderlyingType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return Nullable.GetUnderlyingType(type);
        }

        return type;
    }
}