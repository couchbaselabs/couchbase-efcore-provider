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

    public CouchbaseClientWrapper(IServiceProvider serviceProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder, ILogger<CouchbaseClientWrapper> logger)
    {
        _serviceProvider = serviceProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _logger = logger;
    }

    public string BucketName => _couchbaseDbContextOptionsBuilder.Bucket;

    public async Task<bool> DeleteDocument(string id, (string? scope, string? collection) keyspace)
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

    public async Task<bool> CreateDocument<TEntity>(string id, (string? scope, string? collection) keyspace, TEntity entity)
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

    public async Task<bool> UpdateDocument<TEntity>(string id, (string? scope, string? collection) keyspace, TEntity entity)
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

    private async Task<ICouchbaseCollection> GetCollection((string? scope, string? collection) keyspace)
    {
        try
        {
            var clusterProvider = _serviceProvider.GetRequiredKeyedService<IClusterProvider>(_couchbaseDbContextOptionsBuilder.ClusterOptions.ConnectionString);
            var cluster = await clusterProvider.GetClusterAsync().ConfigureAwait(false);
            _bucket = await cluster.BucketAsync(_couchbaseDbContextOptionsBuilder.Bucket);
        }
        catch (Exception e)
        {
           _logger.LogError(e, "Bucket {BucketName} not found!", _couchbaseDbContextOptionsBuilder.Bucket);
        }

        // ReSharper disable once MethodHasAsyncOverload
        return _bucket.Scope(keyspace.scope).Collection(keyspace.collection);
    }
}