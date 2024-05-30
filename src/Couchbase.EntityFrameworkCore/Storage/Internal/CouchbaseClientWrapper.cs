using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper(INamedBucketProvider namedBucketProvider, ILogger<CouchbaseClientWrapper> _logger) 
    : ICouchbaseClientWrapper
{
    private IBucket? _bucket;

    public async Task<bool> CreateDocument<TEntity>(string id, TEntity entity)
    {
        bool success;
        try
        {
            var collection = await GetCollection().ConfigureAwait(false);
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
            var collection = await GetCollection().ConfigureAwait(false);
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

    public async Task<bool> DeleteDocument(string id)
    {
        bool success;
        try
        {
            var collection = await GetCollection().ConfigureAwait(false);
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
            var collection = await GetCollection().ConfigureAwait(false);
            var getResult = await collection.GetAsync(id).ConfigureAwait(false);
            entity = getResult.ContentAs<TEntity>();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Select failed");
        }

        return entity;
    }

    private async Task<ICouchbaseCollection> GetCollection()
    {
        try
        {
            _bucket ??= await namedBucketProvider.GetBucketAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
           _logger.LogError(e, "Could not fetch collection");
        }

        return _bucket.DefaultCollection();
    }
}