using System.Collections.Concurrent;
using Couchbase.Core.Exceptions;
using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// ReSharper disable MethodHasAsyncOverload

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper : ICouchbaseClientWrapper
{
    private readonly  ITypeTranscoder _transcoder = new RawJsonTranscoder();
    private IBucket? _bucket;
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly ILogger<CouchbaseClientWrapper> _logger;
    private readonly ConcurrentDictionary<string, ICouchbaseCollection> _collectionCache = new();
    private readonly SemaphoreSlim _semaphore = new(1);

    public CouchbaseClientWrapper(IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder, ILogger<CouchbaseClientWrapper> logger)
    {
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _logger = logger;
    }

    public string BucketName => _couchbaseDbContextOptionsBuilder.Bucket;

    public async Task<bool> DeleteDocument(string id, string keyspace)
    {
        bool success;
        try
        {
            var collection = await GetCollection(keyspace).ConfigureAwait(false);
            await collection.RemoveAsync(id).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Delete failed for key {Id} in keyspace {keyspace}",
                id, keyspace);
        }

        return success;
    }

    public async Task<bool> CreateDocument<TEntity>(string id, string keyspace, TEntity entity)
    {
        bool success;
        try
        {
            var collection = await GetCollection(keyspace).ConfigureAwait(false);
            await collection.InsertAsync(id, entity, new InsertOptions().Transcoder(_transcoder)).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Insert failed for key {Id} in keyspace {keyspace}",
                id, keyspace);
        }

        return success;
    }

    public async Task<bool> UpdateDocument<TEntity>(string id, string keyspace, TEntity entity)
    {
        bool success;
        try
        {
            var collection = await GetCollection(keyspace).ConfigureAwait(false);
            await collection.UpsertAsync(id, entity, new UpsertOptions().Transcoder(_transcoder)).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Update failed for key {Id} in keyspace {keyspace}",
                id, keyspace);
        }

        return success;
    }

    private async Task<ICouchbaseCollection> GetCollection(string keyspace)
    {
        if(_collectionCache.TryGetValue(keyspace, out var collection)) return collection;

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _bucket = await _bucketProvider.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).ConfigureAwait(false);

            //Note that the keyspace is stored as Collection.Bucket.Scope in the entity
            //this is done so that the correct alias is chosen as the first letter of the
            //collection that the entity is mapped to and so that we can reuse the sealed
            //TableExpression class instead of bringing it into this project and subclassing
            //it. We may want to take this approach in the future as this can be confusing.
            var splitKeyspace = keyspace.Split('.');
            collection = _bucket.Scope(splitKeyspace[2].TrimEnd('`').TrimStart('`')).Collection(splitKeyspace[0].TrimEnd('`').TrimStart('`'));

            _collectionCache.TryAdd(keyspace, collection);
            return collection;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Could not find collection for keypace {keyspace}.");
            throw new CollectionNotFoundException($"Could not find collection for keypace {keyspace}", e);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}