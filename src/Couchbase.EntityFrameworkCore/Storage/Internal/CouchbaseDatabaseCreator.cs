using System.Diagnostics;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseCreator :  RelationalDatabaseCreator
{
    private readonly IDatabase _database;
    private readonly IDesignTimeModel _designTimeModel;
    private readonly ILogger<CouchbaseDatabaseCreator> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    // Lazily initialized by InitializeAsync before any use; null-forgiving avoids cascading
    // nullable warnings at the (guaranteed-initialized) deref sites.
    private ICluster _cluster = null!;

    public CouchbaseDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies,
        IDatabase database,
        IServiceProvider serviceProvider,
        IDesignTimeModel designTimeModel,
        ILogger<CouchbaseDatabaseCreator> logger,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder,
        ISqlGenerationHelper sqlGenerationHelper) : base(dependencies)
    {
        _database = database;
        _designTimeModel = designTimeModel;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _sqlGenerationHelper = sqlGenerationHelper;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_cluster != null)
        {
            return;
        }

        var clusterProvider = _serviceProvider.GetRequiredService<IClusterProvider>();
        _cluster = await clusterProvider.GetClusterAsync(cancellationToken);
    }

    private Task<IBucket> GetBucketAsync(CancellationToken cancellationToken = default)
        => GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket, cancellationToken);

    private async Task<IBucket> GetBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 10;
        const int delayMs = 500;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _cluster.BucketAsync(bucketName);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (attempt == maxRetries)
                {
                    _logger.LogError(e, "Failed to retrieve Couchbase bucket '{BucketName}' after {MaxRetries} attempts",
                        bucketName, maxRetries);
                    throw;
                }

                _logger.LogWarning(e, "Couchbase bucket '{BucketName}' could not be retrieved (attempt {Attempt}/{MaxRetries}). Retrying...",
                    bucketName, attempt, maxRetries);

                await Task.Delay(delayMs, cancellationToken);
            }
        }

        // Unreachable, but required for compiler
        throw new UnreachableException();
    }

    public override bool HasTables()
    {
        return true;
    }

    /// <summary>
    /// Groups every non-owned entity's resolved keyspace (bucket/scope/collection) by bucket. A
    /// single DbContext may map entities to multiple buckets on the same cluster, so schema
    /// operations (scope/collection/index creation) must target the bucket named by each entity's
    /// keyspace rather than only the configured bucket. Shared by <see cref="CreateCollectionsAsync"/>
    /// and <see cref="CreateIndexesAsync"/> so both stay multi-bucket-aware in the same way.
    /// </summary>
    private Dictionary<string, List<(string Scope, string Collection, string EntityName)>> GetEntityKeyspacesByBucket()
    {
        var configuredScope = _couchbaseDbContextOptionsBuilder.Scope;

        var byBucket = new Dictionary<string, List<(string Scope, string Collection, string EntityName)>>();

        // Always process the configured bucket so its configured scope is ensured even when no
        // entity maps to it (preserves the pre-multi-bucket behavior).
        byBucket[_couchbaseDbContextOptionsBuilder.Bucket] =
            new List<(string Scope, string Collection, string EntityName)>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            // Skip owned types explicitly (matches CouchbaseModelBuilderExtensions.ConfigureToCouchbase)
            // rather than relying solely on them having no table name of their own — they're
            // embedded in their owner's document and have no keyspace to create/index.
            if (entityType.IsOwned())
            {
                continue;
            }

            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            string bucketName;
            string collectionName;
            string scopeName;

            if (CouchbaseKeyspace.TryParse(tableName, out var keyspace))
            {
                bucketName = keyspace!.Value.Bucket;
                collectionName = keyspace.Value.Collection;
                scopeName = keyspace.Value.Scope;
            }
            else
            {
                // Fallback: use table name as collection name in the configured bucket/scope.
                bucketName = _couchbaseDbContextOptionsBuilder.Bucket;
                collectionName = tableName;
                scopeName = configuredScope;
            }

            if (!byBucket.TryGetValue(bucketName, out var entries))
            {
                byBucket[bucketName] = entries = new List<(string Scope, string Collection, string EntityName)>();
            }
            entries.Add((scopeName, collectionName, entityType.ClrType.Name));
        }

        return byBucket;
    }

    private async Task CreateCollectionsAsync(CancellationToken cancellationToken)
    {
        var configuredScope = _couchbaseDbContextOptionsBuilder.Scope;
        var byBucket = GetEntityKeyspacesByBucket();

        foreach (var (bucketName, entries) in byBucket)
        {
            var manager = (await GetBucketAsync(bucketName, cancellationToken)).Collections;
            var existingScopes = (await manager.GetAllScopesAsync(new GetAllScopesOptions().CancellationToken(cancellationToken)))
                .Select(s => s.Name).ToHashSet();

            // Only ensure scopes we will actually create a collection in: the configured scope
            // (always created) and, when AutoCreateScopes is enabled, any other scope. Scopes
            // that would only ever hold skipped collections are left alone so we don't create
            // empty scopes — or trip permission failures — in buckets that don't need them.
            var scopesToEnsure = entries
                .Where(e => e.Scope == configuredScope || _couchbaseDbContextOptionsBuilder.AutoCreateScopes)
                .Select(e => e.Scope)
                .ToHashSet();

            // The configured bucket always ensures the configured scope, even with an empty
            // model (preserves the pre-multi-bucket behavior).
            if (bucketName == _couchbaseDbContextOptionsBuilder.Bucket)
            {
                scopesToEnsure.Add(configuredScope);
            }

            foreach (var scope in scopesToEnsure)
            {
                if (existingScopes.Contains(scope))
                {
                    continue;
                }
                try
                {
                    await manager.CreateScopeAsync(scope, new CreateScopeOptions().CancellationToken(cancellationToken));
                    _logger.LogDebug("Created scope {ScopeName} in bucket {BucketName}", scope, bucketName);
                }
                catch (ScopeExistsException)
                {
                    // Scope already exists, continue
                }
            }

            foreach (var (scopeName, collectionName, entityName) in entries)
            {
                // Skip non-default scope collections if AutoCreateScopes is disabled
                if (scopeName != configuredScope && !_couchbaseDbContextOptionsBuilder.AutoCreateScopes)
                {
                    _logger.LogWarning(
                        "Collection '{CollectionName}' for entity '{EntityName}' targets non-default scope '{ScopeName}' " +
                        "in bucket '{BucketName}' and will not be auto-created. The scope may not exist. " +
                        "Create the scope and collection manually, or enable AutoCreateScopes in DbContext options.",
                        collectionName, entityName, scopeName, bucketName);
                    continue;
                }

                try
                {
                    await manager.CreateCollectionAsync(scopeName, collectionName, new CreateCollectionSettings(),
                        new CreateCollectionOptions().CancellationToken(cancellationToken));
                }
                catch (CollectionExistsException)
                {
                    _logger.LogWarning("Couchbase collection {Keyspace} already exists.",
                        new CouchbaseKeyspace(bucketName, scopeName, collectionName).ToSqlString());
                }
            }
        }
    }

    private async Task CreateSequencesAsync(CancellationToken cancellationToken)
    {
        // Collect all unique sequences from the model that should be auto-created
        // Use tuple key (scope, name) to avoid delimiter parsing issues
        var sequences = new Dictionary<(string Scope, string Name), CouchbaseSequenceOptions>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var sequenceName = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value as string;
                if (string.IsNullOrEmpty(sequenceName))
                {
                    continue;
                }

                // Check if auto-create is disabled (defaults to true if annotation not present)
                // Note: Use pattern matching for unboxing; 'as bool?' doesn't work for boxed value types
                var autoCreateAnnotation = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation);
                var autoCreate = autoCreateAnnotation?.Value is bool b ? b : true;
                if (!autoCreate)
                {
                    _logger.LogDebug("Skipping auto-creation of sequence {SequenceName} (AutoCreate = false)", sequenceName);
                    continue;
                }

                // Get scope override or use default
                var scopeOverride = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value as string;
                var sequenceScope = scopeOverride ?? _couchbaseDbContextOptionsBuilder.Scope;

                // Skip auto-creation for sequences in non-default scopes (scope may not exist)
                if (scopeOverride != null && scopeOverride != _couchbaseDbContextOptionsBuilder.Scope)
                {
                    var propertyPath = $"{property.DeclaringType.ClrType.Name}.{property.Name}";
                    _logger.LogWarning(
                        "Sequence '{SequenceName}' for property '{PropertyPath}' targets non-default scope '{SequenceScope}' " +
                        "and will not be auto-created. The scope may not exist. " +
                        "Create the scope and sequence manually, or use the default scope, or set AutoCreate = false to suppress this warning.",
                        sequenceName, propertyPath, sequenceScope);
                    continue;
                }

                // Get options or use default
                var options = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value as CouchbaseSequenceOptions
                    ?? CouchbaseSequenceOptions.Default;

                var key = (sequenceScope, sequenceName);
                if (sequences.TryGetValue(key, out var existingOptions))
                {
                    // Check for conflicting options
                    if (existingOptions != options)
                    {
                        var propertyPath = $"{property.DeclaringType.ClrType.Name}.{property.Name}";
                        throw new InvalidOperationException(
                            $"Conflicting sequence options detected for sequence '{sequenceName}' in scope '{sequenceScope}'. " +
                            $"Property '{propertyPath}' specifies different options than a previously configured property. " +
                            $"Existing: {existingOptions.ToSqlOptionsClause()}, Conflicting: {options.ToSqlOptionsClause()}. " +
                            $"Ensure all properties using the same sequence have identical options.");
                    }
                }
                else
                {
                    sequences[key] = options;
                }
            }
        }

        // Create each sequence
        foreach (var ((scope, name), options) in sequences)
        {
            await CreateSequenceAsync(scope, name, options, cancellationToken);
        }
    }

    private async Task CreateSequenceAsync(string scope, string sequenceName, CouchbaseSequenceOptions options, CancellationToken cancellationToken)
    {
        var bucket = await GetBucketAsync(cancellationToken);
        var scopeObj = await bucket.ScopeAsync(scope);

        // Build CREATE SEQUENCE statement using proper identifier escaping
        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(_couchbaseDbContextOptionsBuilder.Bucket);
        var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(scope);
        var sequenceIdentifier = _sqlGenerationHelper.DelimitIdentifier(sequenceName);

        var sql = $"CREATE SEQUENCE IF NOT EXISTS {bucketIdentifier}.{scopeIdentifier}.{sequenceIdentifier} {options.ToSqlOptionsClause()}";

        _logger.LogDebug("Creating sequence: {Sql}", sql);

        using var result = await scopeObj.QueryAsync<dynamic>(sql, new QueryOptions().CancellationToken(cancellationToken));

        // Drain all rows to ensure query completes
        await foreach (var _ in result.Rows)
        {
        }
    }

    /// <summary>
    /// Creates a primary index on every collection referenced by the model, when
    /// <see cref="ICouchbaseDbContextOptionsBuilder.AutoCreateIndexes"/> is enabled, and waits for
    /// each index to report online before returning.
    /// </summary>
    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        if (!_couchbaseDbContextOptionsBuilder.AutoCreateIndexes)
        {
            _logger.LogDebug("Skipping auto-creation of primary indexes (AutoCreateIndexes = false)");
            return;
        }

        var configuredScope = _couchbaseDbContextOptionsBuilder.Scope;
        var byBucket = GetEntityKeyspacesByBucket();
        // A HashSet, not a List: TPH inheritance (and any other case where multiple entity types
        // map to the same collection — see the Person/Student/Instructor example in modeling.md)
        // means GetEntityKeyspacesByBucket() can yield the same (bucket, scope, collection) more
        // than once. CouchbaseKeyspace is a readonly record struct with value equality, so a
        // HashSet naturally dedupes to one CREATE PRIMARY INDEX / online-wait per collection
        // instead of once per entity type sharing it.
        var keyspaces = new HashSet<CouchbaseKeyspace>();

        foreach (var (bucketName, entries) in byBucket)
        {
            foreach (var (scopeName, collectionName, entityName) in entries)
            {
                // A collection that CreateCollectionsAsync skipped (non-default scope with
                // AutoCreateScopes disabled) was never created, so there is nothing to index.
                if (scopeName != configuredScope && !_couchbaseDbContextOptionsBuilder.AutoCreateScopes)
                {
                    _logger.LogDebug(
                        "Skipping primary index for collection '{CollectionName}' (entity '{EntityName}') " +
                        "targeting non-default scope '{ScopeName}' with AutoCreateScopes disabled.",
                        collectionName, entityName, scopeName);
                    continue;
                }

                keyspaces.Add(new CouchbaseKeyspace(bucketName, scopeName, collectionName));
            }
        }

        foreach (var keyspace in keyspaces)
        {
            await CreatePrimaryIndexAsync(keyspace, cancellationToken);
        }

        // CREATE PRIMARY INDEX can return before the index is online/queryable, so a query issued
        // immediately after EnsureCreatedAsync returns could otherwise fail. Wait until every
        // primary index reports state='online' before returning.
        foreach (var keyspace in keyspaces)
        {
            await WaitForIndexOnlineAsync(keyspace, cancellationToken);
        }
    }

    private async Task CreatePrimaryIndexAsync(CouchbaseKeyspace keyspace, CancellationToken cancellationToken)
    {
        var bucket = await GetBucketAsync(keyspace.Bucket, cancellationToken);
        var scopeObj = await bucket.ScopeAsync(keyspace.Scope);

        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Bucket);
        var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Scope);
        var collectionIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Collection);

        var sql = $"CREATE PRIMARY INDEX IF NOT EXISTS ON {bucketIdentifier}.{scopeIdentifier}.{collectionIdentifier}";

        _logger.LogDebug("Creating primary index: {Sql}", sql);

        // A collection just created by CreateCollectionsAsync may not be visible to the query
        // service yet — the management API's CreateCollectionAsync can return before the query
        // service's metadata cache picks up the new keyspace, so the very next statement can fail
        // with "Keyspace not found" even though the collection genuinely exists. Retry rather than
        // fail EnsureCreatedAsync over what is normally a sub-second propagation delay.
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var result = await scopeObj.QueryAsync<dynamic>(sql, new QueryOptions().CancellationToken(cancellationToken));

                // Drain all rows to ensure query completes
                await foreach (var _ in result.Rows)
                {
                }

                return;
            }
            // Cancellation must propagate immediately, not be treated as a transient failure to
            // retry — retrying here would turn "stop now" into "keep trying for up to 10 more
            // attempts."
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxAttempts)
            {
                _logger.LogDebug(ex,
                    "Primary index creation for {Keyspace} failed (attempt {Attempt}/{MaxAttempts}); retrying...",
                    keyspace.ToSqlString(), attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private async Task WaitForIndexOnlineAsync(CouchbaseKeyspace keyspace, CancellationToken cancellationToken)
    {
        // Per-keyspace deadline: a shared deadline would let time spent waiting on earlier
        // keyspaces eat into the budget for later ones, causing spurious timeouts.
        var onlineDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        Exception? lastError = null;

        while (true)
        {
            var online = false;
            try
            {
                using var result = await _cluster.QueryAsync<int>(
                    "SELECT RAW COUNT(*) FROM system:indexes WHERE is_primary = true AND state = 'online' "
                    + "AND bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection",
                    new QueryOptions()
                        .Parameter("bucket", keyspace.Bucket)
                        .Parameter("scope", keyspace.Scope)
                        .Parameter("collection", keyspace.Collection)
                        .CancellationToken(cancellationToken));
                await foreach (var count in result.Rows)
                {
                    online = count > 0;
                    break;
                }
                lastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Transient (query service busy right after DDL); keep polling until the deadline
                // check below decides to throw. Cancellation is excluded so it propagates
                // immediately instead of being treated as transient.
                lastError = ex;
            }

            // system:indexes reporting 'online' means the index structure exists; confirm it's
            // actually queryable with one real trial query (RequestPlus, so it waits for the
            // indexer rather than answering from a stale cache) before trusting it.
            if (online && await ConfirmQueryableAsync(keyspace, cancellationToken))
            {
                _logger.LogDebug("Primary index online and queryable for {Keyspace}", keyspace.ToSqlString());
                return;
            }

            if (DateTime.UtcNow > onlineDeadline)
            {
                throw new TimeoutException(
                    $"Primary index for {keyspace.ToSqlString()} did not come online within 60 seconds.", lastError);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task<bool> ConfirmQueryableAsync(CouchbaseKeyspace keyspace, CancellationToken cancellationToken)
    {
        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Bucket);
        var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Scope);
        var collectionIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Collection);
        var sql = $"SELECT 1 FROM {bucketIdentifier}.{scopeIdentifier}.{collectionIdentifier} LIMIT 1";

        try
        {
            using var trialResult = await _cluster.QueryAsync<int>(
                sql, new QueryOptions().ScanConsistency(QueryScanConsistency.RequestPlus).CancellationToken(cancellationToken));
            await foreach (var _ in trialResult.Rows)
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Trial query for {Keyspace} failed; index not yet queryable.", keyspace.ToSqlString());
            return false;
        }
    }

    private async Task DropSequencesAsync(CancellationToken cancellationToken)
    {
        var bucket = await GetBucketAsync(cancellationToken);

        // Collect all unique sequences from the model
        var sequences = new HashSet<(string Scope, string Name)>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var sequenceName = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value as string;
                if (string.IsNullOrEmpty(sequenceName))
                {
                    continue;
                }

                var sequenceScope = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value as string
                    ?? _couchbaseDbContextOptionsBuilder.Scope;

                sequences.Add((sequenceScope, sequenceName));
            }
        }

        // Drop each sequence
        foreach (var (scope, sequenceName) in sequences)
        {
            try
            {
                var scopeObj = await bucket.ScopeAsync(scope);

                // Use proper identifier escaping
                var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(_couchbaseDbContextOptionsBuilder.Bucket);
                var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(scope);
                var sequenceIdentifier = _sqlGenerationHelper.DelimitIdentifier(sequenceName);

                var sql = $"DROP SEQUENCE IF EXISTS {bucketIdentifier}.{scopeIdentifier}.{sequenceIdentifier}";

                _logger.LogDebug("Dropping sequence: {Sql}", sql);

                using var result = await scopeObj.QueryAsync<dynamic>(sql, new QueryOptions().CancellationToken(cancellationToken));

                // Drain all rows to ensure query completes
                await foreach (var _ in result.Rows)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Suppress during cleanup - sequence/scope may not exist, bucket may be deleted next.
                // Log at Warning so unexpected failures are visible. Cancellation is excluded so it
                // stops this loop immediately instead of being logged and swallowed while cleanup
                // of the remaining sequences continues.
                _logger.LogWarning(ex, "Failed to drop sequence {SequenceName} in scope {Scope}", sequenceName, scope);
            }
        }
    }

    /// <summary>
    ///     Creates the physical database. Does not attempt to populate it with any schema.
    /// </summary>
    public override void Create()
    {
        throw ExceptionHelper.SyncroIONotSupportedException();
    }

    public override async Task CreateAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        await InitializeAsync(cancellationToken);

        var manager = _cluster.Buckets;
        try
        {
            await manager.CreateBucketAsync(new BucketSettings
            {
                Name = _couchbaseDbContextOptionsBuilder.Bucket,
                BucketType = BucketType.Couchbase,
                RamQuotaMB = 100,
                FlushEnabled = true
            }, new CreateBucketOptions().CancellationToken(cancellationToken));
        }
        catch (BucketExistsException)
        {
            _logger.LogWarning("Couchbase bucket already exists.");
        }
    }

    /// <summary>
    ///     Deletes the physical database.
    /// </summary>
    public override void Delete()
    {
        throw ExceptionHelper.SyncroIONotSupportedException();
    }

    /// <summary>
    ///     Determines whether the physical database exists. No attempt is made to determine if the database
    ///     contains the schema for the current model.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the database exists; otherwise <see langword="false" />.
    /// </returns>
    public override bool Exists()
    {
#if DEBUG
        return ExistsAsync().GetAwaiter().GetResult();
#else
        throw ExceptionHelper.SyncroIONotSupportedException();
#endif
    }

    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        await InitializeAsync(cancellationToken);

        var manager = _cluster.Buckets;

        try
        {
            await manager.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket,
                new GetBucketOptions().CancellationToken(cancellationToken));
        }
        catch (BucketNotFoundException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Asynchronously ensures that the database for the context exists.
    /// If the bucket does not exist, it is created.
    /// Scopes, collections, and sequences are always created if they don't exist,
    /// regardless of whether the bucket already existed. A primary index is additionally created
    /// on every collection — and waited for online — when
    /// <see cref="ICouchbaseDbContextOptionsBuilder.AutoCreateIndexes"/> is enabled.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the bucket was created; <see langword="false"/> if it already existed.
    /// Note: scopes, collections, and sequences are created in both cases.
    /// </returns>
    public override async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        var created = false;

        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await CreateAsync(cancellationToken).ConfigureAwait(false);
            created = true;
        }

        // Always ensure scopes, collections, and sequences exist even if the bucket already
        // existed (they use IF NOT EXISTS / catch-exists patterns). CreateCollectionsAsync
        // ensures the required scopes per bucket before creating collections. CreateIndexesAsync
        // runs last since it needs the collections it indexes to already exist, and is a no-op
        // unless AutoCreateIndexes is enabled.
        await CreateCollectionsAsync(cancellationToken);
        await CreateSequencesAsync(cancellationToken);
        await CreateIndexesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Asynchronously deletes the database.
    /// </summary>
    public override async Task DeleteAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        await InitializeAsync(cancellationToken);

        // Only attempt to drop sequences if the bucket exists
        // GetBucketAsync retries up to 10 times, so we check existence first to fail fast
        if (await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await DropSequencesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Cancellation is excluded so DeleteAsync stops here instead of silently
                // continuing on to drop the bucket after the caller asked to stop.
                _logger.LogWarning(ex, "Failed to drop sequences during delete.");
            }
        }

        var manager = _cluster.Buckets;
        try
        {
            await manager.DropBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket,
                new DropBucketOptions().CancellationToken(cancellationToken));
        }
        catch (BucketNotFoundException)
        {
            _logger.LogWarning("Couchbase bucket not found during delete.");
        }
    }
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
