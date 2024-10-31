using System.Collections.Concurrent;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.VisualBasic;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper(DatabaseDependencies dependencies, ICouchbaseClientWrapper couchbaseClient, INamedCollectionProvider namedCollectionProvider)
    : Database(dependencies)
{
    private readonly ConcurrentDictionary<IEntityType, (string scope, string collection)> _keyspaceCache = new ();

    public string? ScopeName => namedCollectionProvider.ScopeName;

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
            var keyspace = GetKeySpace(updateEntry);
            var document= GenerateRootJson(updateEntry);

            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                case EntityState.Deleted:
                    if (await couchbaseClient.DeleteDocument(primaryKey, keyspace).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Modified:
                    if (await couchbaseClient.UpdateDocument(primaryKey, keyspace, document).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Added:
                {
                    if (await couchbaseClient.CreateDocument(primaryKey, keyspace, document).ConfigureAwait(false))
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

                        break;
                    case "Int32":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (int)value);
                        }

                        break;
                    case "DateTime":
                        if (value != null)
                        {
                            writer.WriteString(fieldName, (DateTime)value);
                        }

                        break;
                    case "Decimal":
                        if (value != null)
                        {
                            writer.WriteNumber(fieldName, (decimal)value);
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

    private bool TryGetKeyspaceFromModel(string scopeAndCollection, out string? scope, out string? collection)
    {
        var delimitedScopeAndCollection = scopeAndCollection.Split(".");
        if (delimitedScopeAndCollection.Length == 2)
        {
            scope = delimitedScopeAndCollection[0];
            collection = delimitedScopeAndCollection[1];
            return true;
        }

        if (ScopeName == null || delimitedScopeAndCollection.Length == 0)
        {
            scope = null;
            collection = null;
            return false;
        }
        scope = ScopeName;
        collection = delimitedScopeAndCollection[0];
        return true;
    }

    private (string? scope, string? collection) GetKeySpace(IUpdateEntry updateEntry)
    {
        var entityEntry = updateEntry.ToEntityEntry();
        var entity = entityEntry.Entity;
        var entityType = updateEntry.EntityType;

        //Check if keyspace is cached
        if (_keyspaceCache.TryGetValue(entityType, out var keyspace))
        {
            return keyspace;
        }

        //Finally check the model mapping
        if (TryGetKeyspaceFromModel(entityType.GetCollectionName(), out var scope, out var collection))
        {
            _keyspaceCache.TryAdd(entityType, (scope, collection));
            return (scope, collection);
        }

        throw new KeyspaceNotFoundException(entityType.Name);
    }
}