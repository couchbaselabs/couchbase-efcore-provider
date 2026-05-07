using System.Diagnostics;
using Couchbase.Diagnostics;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Google.Api;
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
        var clusterProvider = _serviceProvider.GetRequiredService<IClusterProvider>();
        _cluster = await clusterProvider.GetClusterAsync();
    }

    private async Task<IBucket> GetBucketAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        IBucket bucket = null;
        var exists = false;
        do
        {
            try
            {
                bucket = await _cluster.BucketAsync(_couchbaseDbContextOptionsBuilder.Bucket);
                exists = true;
            }
            catch (Exception e)
            {
                _logger.LogWarning("Couchbase bucket could not be retrieved. {0}", e);
            }
        }while (!exists);

        Debug.Assert(bucket != null, nameof(bucket) + " != null");
        return bucket;
    }

    private async Task<bool> ScopeExistsAsync()
    {
        var exists = false;
        var manager = (await GetBucketAsync()).Collections;
        try
        {
            var scopes = await manager.GetAllScopesAsync();
            if (scopes.Contains(new ScopeSpec(_couchbaseDbContextOptionsBuilder.Scope)))
            {
                exists = true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        return exists;
    }

    private async Task<bool> CollectionsExistsAsync()
    {
        var manager = (await GetBucketAsync()).Collections;
        var scopes = await manager.GetAllScopesAsync();

        try
        {
            var scope = scopes.FirstOrDefault(x => x.Name == _couchbaseDbContextOptionsBuilder.Scope);
            var entityTypes = _designTimeModel.Model.GetEntityTypes();
            foreach (var entityType in entityTypes)
            {
                if (scope!.Collections.Contains(new CollectionSpec(scope.Name, entityType.Name.Split('+')[1])))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Couchbase collection could not be retrieved.");
        }

        return false;
    }

    public override bool HasTables()
    {
        return true;
    }

    private async Task CreateScopeAsync()
    {
        var manager = (await GetBucketAsync()).Collections;
        var scopes = await manager.GetAllScopesAsync();
        if(!scopes.Contains(new ScopeSpec(_couchbaseDbContextOptionsBuilder.Bucket)))
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
        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            try
            {
               await manager.CreateCollectionAsync(_couchbaseDbContextOptionsBuilder.Scope, 
                   entityType.Name.Split('+')[1], new CreateCollectionSettings());
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
        var sequences = new Dictionary<string, (string Scope, CouchbaseSequenceOptions Options)>();

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
                var autoCreateAnnotation = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation);
                var autoCreate = autoCreateAnnotation?.Value as bool? ?? true;
                if (!autoCreate)
                {
                    _logger.LogDebug("Skipping auto-creation of sequence {SequenceName} (AutoCreate = false)", sequenceName);
                    continue;
                }

                // Get scope override or use default
                var sequenceScope = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value as string
                    ?? _couchbaseDbContextOptionsBuilder.Scope;

                // Get options or use default
                var options = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value as CouchbaseSequenceOptions
                    ?? CouchbaseSequenceOptions.Default;

                // Use scope.sequenceName as key to handle sequences in different scopes
                var key = $"{sequenceScope}.{sequenceName}";
                if (!sequences.ContainsKey(key))
                {
                    sequences[key] = (sequenceScope, options);
                }
            }
        }

        // Create each sequence
        foreach (var (key, (sequenceScope, options)) in sequences)
        {
            var sequenceName = key.Split('.').Last();
            await CreateSequenceAsync(sequenceScope, sequenceName, options);
        }
    }

    private async Task CreateSequenceAsync(string scope, string sequenceName, CouchbaseSequenceOptions options)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create sequence {SequenceName} in scope {Scope}", sequenceName, scope);
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
    /// Asynchronously ensures that the database for the context exists. If it exists, no action is taken.
    /// If it does not exist then the database and all its schema are created. If the database exists, then
    /// no effort is made to ensure it is compatible with the model for this context.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        if (!await ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await CreateAsync(cancellationToken).ConfigureAwait(false);
            await CreateScopeAsync();
            await CreateCollectionsAsync();
            await CreateSequencesAsync();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Asynchronously deletes the database.
    /// </summary>
    public override async Task DeleteAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        await InitializeAsync(cancellationToken);

        // Only attempt to drop sequences if the bucket exists
        // GetBucketAsync retries indefinitely, so we check existence first
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
