using System.Collections.Concurrent;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
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
       return Task.Run(async () => await SaveChangesAsync(entries).ConfigureAwait(false)).Result;
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
            var document= GenerateRootJson(updateEntry);

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
                    if (await couchbaseClient.UpdateDocument(primaryKey,  entityType.GetCollectionName(), document).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Added:
                {
                    if (await couchbaseClient.CreateDocument(primaryKey,  entityType.GetCollectionName(), document).ConfigureAwait(false))
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
                        if (value != null)
                        {
                            writer.WriteString(fieldName, (string)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "Single":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (float)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "Int16":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (short)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "UInt16":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (ushort)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "Int32":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (int)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "UInt32":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (uint)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "DateTime":
                        if (value != null)
                        {
                            writer.WriteString(fieldName, (DateTime)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "Decimal":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (decimal)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "Byte[]":
                        if (value != null)
                        {
                            writer.WriteBase64String(property.Name, new ReadOnlyMemory<byte>((byte[])value).Span);
                        }

                        break;
                    case "Guid":
                        if (value != null)
                        {
                            writer.WriteString(property.Name, value.ToString());
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }

                        break;
                    case "Boolean":
                        if (value != null)
                        {
                            writer.WriteBoolean(property.Name, (bool)value);
                        }
                        else
                        {
                            writer.WriteNull(property.Name);
                        }
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

    private Type GetUnderlyingType(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}
