using System.Reflection;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper 
    : ICouchbaseClientWrapper
{
    private readonly INamedBucketProvider _namedBucketProvider;
    private readonly ILogger<CouchbaseClientWrapper> _logger;

    public CouchbaseClientWrapper(INamedBucketProvider namedBucketProvider, ILogger<CouchbaseClientWrapper> logger)
    {
        _namedBucketProvider = namedBucketProvider;
        _logger = logger;
    }
    private IBucket? _bucket;

    public async Task<bool> CreateDocument<TEntity>(string id, TEntity entity)
    {
        bool success;
        try
        {
            var contextId = GetContextId(entity);
            var collection = await GetCollection(contextId.scope, contextId.collection).ConfigureAwait(false);
            await collection.InsertAsync(id, entity).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Insert failed");
        }

        return success;
    }

    public async Task<bool> UpdateDocument<TEntity>(string id, TEntity entity)
    {
        bool success;
        try
        {
            var contextId = GetContextId(entity);
            var collection = await GetCollection(contextId.scope, contextId.collection).ConfigureAwait(false);
            await collection.UpsertAsync(id, entity).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Update failed");
        }

        return success;
    }

    public async Task<bool> DeleteDocument<TEntity>(string id, TEntity entity)
    {
        bool success;
        try
        {
            var contextId = GetContextId(entity);
            var collection = await GetCollection(contextId.scope, contextId.collection).ConfigureAwait(false);
            await collection.RemoveAsync(id).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Delete failed");
        }

        return success;
    }

    public async Task<TEntity> SelectDocument<TEntity>(string id)
    {
        TEntity entity = default;
        try
        {
            var contextId = GetContextId(entity);
            var collection = await GetCollection(contextId.scope, contextId.collection).ConfigureAwait(false);
            var getResult = await collection.GetAsync(id).ConfigureAwait(false);
            entity = getResult.ContentAs<TEntity>();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Select failed");
        }

        return entity;
    }

    private async Task<ICouchbaseCollection> GetCollection(string scope, string collection)
    {
        try
        {
            _bucket ??= await _namedBucketProvider.GetBucketAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
           _logger.LogError(e, "Could not fetch collection");
        }

        return _bucket.Scope(scope).Collection(collection);
    }

    private static (string bucket, string scope, string collection) GetContextId(object entity)
    {
        var attribute = (CouchbaseAttribute)entity.
            GetType().GetCustomAttributes().
            First(x => x.GetType() == typeof(CouchbaseAttribute));

        return (attribute.Bucket, attribute.Scope, attribute.Collection);
    }
}