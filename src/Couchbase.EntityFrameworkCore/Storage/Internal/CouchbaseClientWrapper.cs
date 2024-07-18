using System.Reflection;
using Couchbase.Core.IO.Serializers;
using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper : ICouchbaseClientWrapper
{
    private readonly INamedBucketProvider _namedBucketProvider;
    private readonly ILogger<CouchbaseClientWrapper> _logger;
    private IBucket? _bucket;
    public CouchbaseClientWrapper(INamedBucketProvider namedBucketProvider, ILogger<CouchbaseClientWrapper> logger)
    {
        _namedBucketProvider = namedBucketProvider;
        _logger = logger;
    }

    public async Task<bool> DeleteDocument<TEntity>(string id, string? contextId, TEntity entity)
    {
        bool success;
        try
        {
            var contextIdTuple = DelimitContextId(contextId);
            var collection = await GetCollection(contextIdTuple.scope, contextIdTuple.collection).ConfigureAwait(false);
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

    public async Task<bool> CreateDocument<TEntity>(string id, string contextId, TEntity entity)
    {
        bool success;
        try
        {
            var serializer =  new JsonTranscoder(new DefaultSerializer(
                new JsonSerializerSettings
                {
                    // PreserveReferencesHandling = PreserveReferencesHandling.Objects
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                },
                new JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects
                }));
            var contextIdTuple = DelimitContextId(contextId);
            var collection = await GetCollection(contextIdTuple.scope, contextIdTuple.collection).ConfigureAwait(false);
            await collection.InsertAsync(id, entity, new InsertOptions().Transcoder(serializer)).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e)
        {
            success = false;
            _logger.LogError(e, "Insert failed");
        }

        return success;
    }

    public async Task<bool> UpdateDocument<TEntity>(string id, string? contextId, TEntity entity)
    {
        bool success;
        try
        {
            var contextIdTuple = DelimitContextId(contextId);
            var collection = await GetCollection(contextIdTuple.scope, contextIdTuple.collection).ConfigureAwait(false);
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

    private static (string bucket, string scope, string collection) DelimitContextId(string contextId)
    {
        var delimitedContextId = contextId.Split(".");
        return (delimitedContextId[0], delimitedContextId[1], delimitedContextId[2]);
    }
    
    private static (string bucket, string scope, string collection) GetContextId(object entity)
    {
        var attribute = (CouchbaseKeyspaceAttribute)entity.
            GetType().GetCustomAttributes().
            First(x => x.GetType() == typeof(CouchbaseKeyspaceAttribute));

        return (attribute.Bucket, attribute.Scope, attribute.Collection);
    }
}