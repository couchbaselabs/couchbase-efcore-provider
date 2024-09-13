using System.Collections.Concurrent;
using System.Reflection;
using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.Logging;
// ReSharper disable MethodHasAsyncOverload

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper(INamedBucketProvider namedBucketProvider, ILogger<CouchbaseClientWrapper> logger)
    : ICouchbaseClientWrapper
{
    private readonly ConcurrentDictionary<string, (string scope, string collection)> _keyspaceCache = new ();
    private readonly  ITypeTranscoder _transcoder = new RawJsonTranscoder();
    private IBucket? _bucket;

    public async Task<bool> DeleteDocument(string id, string? scopeAndCollection)
    {
        bool success;
        try
        {
            var keyspace = GetOrAddKeyspace(scopeAndCollection);
            var collection = await GetCollection(keyspace.scope, keyspace.collection).ConfigureAwait(false);
            await collection.RemoveAsync(id).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            logger.LogError(e, "Delete failed for key {Id} in keyspace {ScopeAndCollection}", 
                id, scopeAndCollection);
        }

        return success;
    }

    public async Task<bool> CreateDocument<TEntity>(string id, string scopeAndCollection, TEntity entity)
    {
        bool success;
        try
        {
            var keyspace = GetOrAddKeyspace(scopeAndCollection);
            var collection = await GetCollection(keyspace.scope, keyspace.collection).ConfigureAwait(false);
            await collection.InsertAsync(id, entity, new InsertOptions().Transcoder(_transcoder)).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            logger.LogError(e, "Insert failed for key {Id} in keyspace {ScopeAndCollection}", 
                id, scopeAndCollection);
        }

        return success;
    }

    public async Task<bool> UpdateDocument<TEntity>(string id, string? scopeAndCollection, TEntity entity)
    {
        bool success;
        try
        {
            var keyspace = GetOrAddKeyspace(scopeAndCollection);
            var collection = await GetCollection(keyspace.scope, keyspace.collection).ConfigureAwait(false);
            await collection.UpsertAsync(id, entity, new UpsertOptions().Transcoder(_transcoder)).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            logger.LogError(e, "Update failed for key {Id} in keyspace {ScopeAndCollection}", 
                id, scopeAndCollection);
        }

        return success;
    }

    public string BucketName => namedBucketProvider.BucketName;

    private async Task<ICouchbaseCollection> GetCollection(string scope, string collection)
    {
        try
        {
            _bucket ??= await namedBucketProvider.GetBucketAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
           logger.LogError(e, "Bucket {BucketName} not found!", namedBucketProvider.BucketName);
        }

        // ReSharper disable once MethodHasAsyncOverload
        return _bucket.Scope(scope).Collection(collection);
    }

    private (string scope, string collection) GetOrAddKeyspace(string scopeAndCollection)
    {
        return _keyspaceCache.GetOrAdd(scopeAndCollection, s =>
        {
            var delimitedScopeAndCollection = s.Split(".");
            return (delimitedScopeAndCollection[0], delimitedScopeAndCollection[1]);
        });
    }
    
    private static (string scope, string collection) GetContextId(object entity)
    {
        var attribute = (CouchbaseKeyspaceAttribute)entity.
            GetType().GetCustomAttributes().
            First(x => x.GetType() == typeof(CouchbaseKeyspaceAttribute));

        return (attribute.Scope, attribute.Collection);
    }
}