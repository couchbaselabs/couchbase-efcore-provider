using System.Diagnostics;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
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
    private ICluster _cluster;

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
        _cluster = await clusterProvider.GetClusterAsync();
    }

    private async Task<IBucket> GetBucketAsync(CancellationToken cancellationToken = default)
    {
        const int maxRetries = 10;
        const int delayMs = 500;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _cluster.BucketAsync(_couchbaseDbContextOptionsBuilder.Bucket);
            }
            catch (Exception e)
            {
                if (attempt == maxRetries)
                {
                    _logger.LogError(e, "Failed to retrieve Couchbase bucket '{BucketName}' after {MaxRetries} attempts",
                        _couchbaseDbContextOptionsBuilder.Bucket, maxRetries);
                    throw;
                }

                _logger.LogWarning(e, "Couchbase bucket '{BucketName}' could not be retrieved (attempt {Attempt}/{MaxRetries}). Retrying...",
                    _couchbaseDbContextOptionsBuilder.Bucket, attempt, maxRetries);

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

    private async Task CreateScopeAsync()
    {
        var manager = (await GetBucketAsync()).Collections;
        var scopes = await manager.GetAllScopesAsync();
        if(!scopes.Contains(new ScopeSpec(_couchbaseDbContextOptionsBuilder.Scope)))
        {
            try
            {
                await manager.CreateScopeAsync(_couchbaseDbContextOptionsBuilder.Scope);
            }
            catch (ScopeExistsException)
            {
                _logger.LogWarning("Couchbase scope already exists.");
            }
        }
    }

    private async Task CreateCollectionsAsync()
    {
        var manager = (await GetBucketAsync()).Collections;

        // Collect all collections and their scopes from the model
        var collections = new List<(string Scope, string Collection, string EntityName)>();
        var nonDefaultScopes = new HashSet<string>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            string collectionName;
            string scopeName;

            if (CouchbaseKeyspace.TryParse(tableName, out var keyspace))
            {
                collectionName = keyspace!.Value.Collection;
                scopeName = keyspace.Value.Scope;
            }
            else
            {
                // Fallback: use table name as collection name in default scope
                collectionName = tableName;
                scopeName = _couchbaseDbContextOptionsBuilder.Scope;
            }

            collections.Add((scopeName, collectionName, entityType.ClrType.Name));

            if (scopeName != _couchbaseDbContextOptionsBuilder.Scope)
            {
                nonDefaultScopes.Add(scopeName);
            }
        }

        // Create non-default scopes if AutoCreateScopes is enabled
        if (_couchbaseDbContextOptionsBuilder.AutoCreateScopes && nonDefaultScopes.Count > 0)
        {
            foreach (var scope in nonDefaultScopes)
            {
                try
                {
                    await manager.CreateScopeAsync(scope);
                    _logger.LogDebug("Created scope {ScopeName}", scope);
                }
                catch (ScopeExistsException)
                {
                    // Scope already exists, continue
                }
            }
        }

        // Create collections
        foreach (var (scopeName, collectionName, entityName) in collections)
        {
            // Skip non-default scope collections if AutoCreateScopes is disabled
            if (scopeName != _couchbaseDbContextOptionsBuilder.Scope && !_couchbaseDbContextOptionsBuilder.AutoCreateScopes)
            {
                _logger.LogWarning(
                    "Collection '{CollectionName}' for entity '{EntityName}' targets non-default scope '{ScopeName}' " +
                    "and will not be auto-created. The scope may not exist. " +
                    "Create the scope and collection manually, or enable AutoCreateScopes in DbContext options.",
                    collectionName, entityName, scopeName);
                continue;
            }

            try
            {
                await manager.CreateCollectionAsync(scopeName, collectionName, new CreateCollectionSettings());
            }
            catch (CollectionExistsException)
            {
                _logger.LogWarning("Couchbase collection already exists.");
            }
        }
    }

    private async Task CreateSequencesAsync()
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
            await CreateSequenceAsync(scope, name, options);
        }
    }

    private async Task CreateSequenceAsync(string scope, string sequenceName, CouchbaseSequenceOptions options)
    {
        var bucket = await GetBucketAsync();
        var scopeObj = await bucket.ScopeAsync(scope);

        // Build CREATE SEQUENCE statement using proper identifier escaping
        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(_couchbaseDbContextOptionsBuilder.Bucket);
        var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(scope);
        var sequenceIdentifier = _sqlGenerationHelper.DelimitIdentifier(sequenceName);

        var sql = $"CREATE SEQUENCE IF NOT EXISTS {bucketIdentifier}.{scopeIdentifier}.{sequenceIdentifier} {options.ToSqlOptionsClause()}";

        _logger.LogDebug("Creating sequence: {Sql}", sql);

        using var result = await scopeObj.QueryAsync<dynamic>(sql);

        // Drain all rows to ensure query completes
        await foreach (var _ in result.Rows)
        {
        }
    }

    private async Task DropSequencesAsync()
    {
        var bucket = await GetBucketAsync();

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

                using var result = await scopeObj.QueryAsync<dynamic>(sql);

                // Drain all rows to ensure query completes
                await foreach (var _ in result.Rows)
                {
                }
            }
            catch (Exception ex)
            {
                // Suppress during cleanup - sequence/scope may not exist, bucket may be deleted next.
                // Log at Warning so unexpected failures are visible.
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
            });
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
            await manager.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket);
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
    /// regardless of whether the bucket already existed.
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

        // Always ensure scope, collections, and sequences exist
        // even if bucket already existed (they use IF NOT EXISTS / catch-exists patterns)
        await CreateScopeAsync();
        await CreateCollectionsAsync();
        await CreateSequencesAsync();

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
                await DropSequencesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drop sequences during delete.");
            }
        }

        var manager = _cluster.Buckets;
        try
        {
            await manager.DropBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket);
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
