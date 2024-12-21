using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// ReSharper disable MethodHasAsyncOverload

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper(IServiceProvider serviceProvider,
    ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder, ILogger<CouchbaseClientWrapper> logger)
    : ICouchbaseClientWrapper
{
    private readonly  ITypeTranscoder _transcoder = new RawJsonTranscoder();
    private IBucket? _bucket;

    public string BucketName => couchbaseDbContextOptionsBuilder.Bucket;

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
            logger.LogError(e, "Delete failed for key {Id} in keyspace {keyspace}",
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
            logger.LogError(e, "Insert failed for key {Id} in keyspace {keyspace}",
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
            logger.LogError(e, "Update failed for key {Id} in keyspace {keyspace}",
                id, keyspace);
        }

        return success;
    }

    private async Task<ICouchbaseCollection> GetCollection((string? scope, string? collection) keyspace)
    {
        try
        {
            var namedBucketProvider = serviceProvider.GetRequiredKeyedService<INamedBucketProvider>(couchbaseDbContextOptionsBuilder
                    .Bucket);
            _bucket ??= await namedBucketProvider.GetBucketAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
           logger.LogError(e, "Bucket {BucketName} not found!", couchbaseDbContextOptionsBuilder.Bucket);
        }

        //possibly switch to INamedCollectionProvider
        // ReSharper disable once MethodHasAsyncOverload
        return _bucket.Scope(keyspace.scope).Collection(keyspace.collection);
    }
}