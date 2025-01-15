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
    private readonly IServiceProvider _serviceProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly ILogger<CouchbaseClientWrapper> _logger;
    private readonly ConcurrentDictionary<string, ICouchbaseCollection> _collectionCache = new();
    private readonly SemaphoreSlim _semaphore = new(1);

    public CouchbaseClientWrapper(IServiceProvider serviceProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder, ILogger<CouchbaseClientWrapper> logger)
    {
        _serviceProvider = serviceProvider;
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
            var clusterProvider =
                _serviceProvider.GetRequiredKeyedService<IClusterProvider>(_couchbaseDbContextOptionsBuilder
                    .ClusterOptions.ConnectionString);
            var cluster = await clusterProvider.GetClusterAsync().ConfigureAwait(false);
            _bucket = await cluster.BucketAsync(_couchbaseDbContextOptionsBuilder.Bucket);

            var splitKeyspace = keyspace.Split('.');
            collection = _bucket.Scope(splitKeyspace[1]).Collection(splitKeyspace[2]);
            
            _collectionCache.TryAdd(keyspace, collection);
            return collection;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Could not find collection for keypace {keyspace}");
            throw new CollectionNotFoundException($"Could not find collection for keypace {keyspace}", e);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}