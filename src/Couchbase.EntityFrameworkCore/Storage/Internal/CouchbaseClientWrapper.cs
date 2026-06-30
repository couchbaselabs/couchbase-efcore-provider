using System.Collections.Concurrent;
using Couchbase.Core.Exceptions;
using Couchbase.Core.IO.Serializers;
using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

// ReSharper disable MethodHasAsyncOverload

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper : ICouchbaseClientWrapper
{
    private IBucket? _bucket;
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly ILogger<CouchbaseClientWrapper> _logger;
    private readonly ConcurrentDictionary<string, CachedKeyspace> _keyspaceCache = new();
    private readonly SemaphoreSlim _semaphore = new(1);

    public CouchbaseClientWrapper(IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder, ILogger<CouchbaseClientWrapper> logger)
    {
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _logger = logger;
    }

    public string BucketName => _couchbaseDbContextOptionsBuilder.Bucket;

    public async Task<bool> DeleteDocument(string id, string keyspace, CancellationToken cancellationToken = default)
    {
        bool success;
        try
        {
            var collection = await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
            await collection.RemoveAsync(id, new RemoveOptions().CancellationToken(cancellationToken)).ConfigureAwait(false);
            success = true;
        }
        catch (OperationCanceledException)
        {
            throw; // Surface cancellation as-is rather than masking it as a DbUpdateException.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Delete failed for key {Id} in keyspace {keyspace}",
                id, keyspace);

            throw new DbUpdateException(
                $"Delete failed for key {id} in keyspace {keyspace}", e);
        }

        return success;
    }

    public async Task<bool> CreateDocument<TEntity>(string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
    {
        bool success;
        try
        {
            var collection = await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
            await collection.InsertAsync(id, entity, new InsertOptions().CancellationToken(cancellationToken)).ConfigureAwait(false);
            success = true;
        }
        catch (OperationCanceledException)
        {
            throw; // Surface cancellation as-is rather than masking it as a DbUpdateException.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Insert failed for key {Id} in keyspace {keyspace}",
                id, keyspace);

            throw new DbUpdateException(
                $"Insert failed for key {id} in keyspace {keyspace}", e);
        }

        return success;
    }

    public async Task<bool> UpdateDocument<TEntity>(string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
    {
        bool success;
        try
        {
            var collection = await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
            await collection.UpsertAsync(id, entity, new UpsertOptions().CancellationToken(cancellationToken)).ConfigureAwait(false);
            success = true;
        }
        catch (OperationCanceledException)
        {
            throw; // Surface cancellation as-is rather than masking it as a DbUpdateException.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Update failed for key {Id} in keyspace {keyspace}",
                id, keyspace);

            throw new DbUpdateException(
                $"Update failed for key {id} in keyspace {keyspace}", e);
        }

        return success;
    }

    public async Task<ICouchbaseCollection> GetCollectionAsync(string keyspace, CancellationToken cancellationToken = default)
    {
        return await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueTransactionalInsert<TEntity>(CouchbaseDbTransaction transaction, string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
    {
        var collection = await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
        transaction.EnqueueInsert(collection, id, entity!);
    }

    public async Task EnqueueTransactionalUpsert<TEntity>(CouchbaseDbTransaction transaction, string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default)
    {
        var collection = await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
        transaction.EnqueueUpsert(collection, id, entity!);
    }

    public async Task EnqueueTransactionalRemove(CouchbaseDbTransaction transaction, string id, string keyspace, CancellationToken cancellationToken = default)
    {
        var collection = await GetCollection(keyspace, cancellationToken).ConfigureAwait(false);
        transaction.EnqueueRemove(collection, id);
    }

    private async Task<ICouchbaseCollection> GetCollection(string keyspace, CancellationToken cancellationToken = default)
    {
        if (_keyspaceCache.TryGetValue(keyspace, out var cached))
        {
            return cached.Collection;
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_keyspaceCache.TryGetValue(keyspace, out cached))
            {
                return cached.Collection;
            }

            var parsed = ParseKeyspace(keyspace);

            // Validate that the keyspace bucket matches the configured bucket
            // This prevents inconsistent behavior where queries target one bucket
            // but KV operations target another
            var configuredBucket = _couchbaseDbContextOptionsBuilder.Bucket;
            if (!string.Equals(parsed.Bucket, configuredBucket, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Keyspace bucket mismatch: The keyspace {parsed.ToSqlString()} specifies bucket '{parsed.Bucket}', " +
                    $"but the DbContext is configured to use bucket '{configuredBucket}'. " +
                    $"Ensure the entity mapping matches the DbContext bucket configuration.");
            }

            _bucket = await _bucketProvider.GetBucketAsync(configuredBucket).ConfigureAwait(false);
            var collection = _bucket.Scope(parsed.Scope).Collection(parsed.Collection);

            _keyspaceCache[keyspace] = new CachedKeyspace(parsed.ToSqlString(), collection);
            return collection;
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw bucket mismatch errors without wrapping
        }
        catch (ArgumentException)
        {
            throw; // Re-throw invalid keyspace format errors without wrapping
        }
        catch (Exception e)
        {
            // Try to get display keyspace for error message, but handle case where parsing itself failed
            string displayKeyspace;
            if (CouchbaseKeyspace.TryParse(keyspace, out var parsed))
            {
                displayKeyspace = parsed.Value.ToSqlString();
            }
            else
            {
                displayKeyspace = keyspace; // Fall back to raw keyspace string
            }
            _logger.LogError(e, "Could not find collection for keyspace {Keyspace}", displayKeyspace);
            throw ExceptionHelper.InvalidKeyspaceFormatOrMissingCollection(displayKeyspace, e);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Parses the keyspace format (Bucket.Scope.Collection) into its components.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="CouchbaseKeyspace.TryParse"/> which validates that all parts are non-empty.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the keyspace format is invalid.</exception>
    private static CouchbaseKeyspace ParseKeyspace(string keyspace)
    {
        if (!CouchbaseKeyspace.TryParse(keyspace, out var parsed))
        {
            throw new ArgumentException(
                $"Invalid keyspace format: '{keyspace}'. Expected format: Bucket.Scope.Collection " +
                $"where all parts are non-empty.",
                nameof(keyspace));
        }

        return parsed.Value;
    }

    private readonly record struct CachedKeyspace(
        string DisplayKeyspace,
        ICouchbaseCollection Collection);
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
