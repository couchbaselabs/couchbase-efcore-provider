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
    private readonly TimeProvider _timeProvider;
    // Lazily initialized by InitializeAsync before any use; null-forgiving avoids cascading
    // nullable warnings at the (guaranteed-initialized) deref sites.
    private ICluster _cluster = null!;

    /// <param name="timeProvider">
    /// Source of time for the retry/online-wait deadlines and delays below. Optional and defaults
    /// to <see cref="TimeProvider.System"/> — nothing needs to register <see cref="TimeProvider"/>
    /// in DI for normal use; tests can pass a <c>FakeTimeProvider</c> (from
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c>) directly to make the 60s/1s/500ms
    /// constants advance deterministically instead of requiring a real wait.
    /// </param>
    public CouchbaseDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies,
        IDatabase database,
        IServiceProvider serviceProvider,
        IDesignTimeModel designTimeModel,
        ILogger<CouchbaseDatabaseCreator> logger,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder,
        ISqlGenerationHelper sqlGenerationHelper,
        TimeProvider? timeProvider = null) : base(dependencies)
    {
        _database = database;
        _designTimeModel = designTimeModel;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _sqlGenerationHelper = sqlGenerationHelper;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var delay = TimeSpan.FromMilliseconds(500);

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

                await Task.Delay(delay, _timeProvider, cancellationToken);
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

            var keyspace = ResolveEntityKeyspace(tableName);

            if (!byBucket.TryGetValue(keyspace.Bucket, out var entries))
            {
                byBucket[keyspace.Bucket] = entries = new List<(string Scope, string Collection, string EntityName)>();
            }
            entries.Add((keyspace.Scope, keyspace.Collection, entityType.ClrType.Name));
        }

        return byBucket;
    }

    /// <summary>
    /// Resolves an entity type's table name into its actual keyspace. Thin wrapper over
    /// <see cref="CouchbaseKeyspace.Resolve"/> using this creator's configured bucket/scope as the
    /// fallback. Shared by <see cref="GetEntityKeyspacesByBucket"/> and
    /// <see cref="CreateSecondaryIndexesAsync"/> so both resolve keyspaces identically.
    /// </summary>
    private CouchbaseKeyspace ResolveEntityKeyspace(string tableName)
        => CouchbaseKeyspace.Resolve(tableName, _couchbaseDbContextOptionsBuilder.Bucket, _couchbaseDbContextOptionsBuilder.Scope);

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
        // Collect all unique sequences from the model that should be auto-created.
        // Keyed by (bucket, scope, name) rather than just (scope, name): a sequence lives at
        // bucket.scope.name, so the same (scope, name) in two DIFFERENT buckets is a genuinely
        // distinct sequence, not a conflict -- see CouchbaseKeyspace.Resolve for how each
        // sequence-owning property's entity's actual bucket is determined (mirrors how
        // GetEntityKeyspacesByBucket() resolves collections/indexes, rather than always assuming
        // the configured bucket).
        var sequences = new Dictionary<(string Bucket, string Scope, string Name), CouchbaseSequenceOptions>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            var sequenceBucket = CouchbaseKeyspace.ResolveBucket(
                entityType.GetTableName(), _couchbaseDbContextOptionsBuilder.Bucket);

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

                var key = (sequenceBucket, sequenceScope, sequenceName);
                if (sequences.TryGetValue(key, out var existingOptions))
                {
                    // Check for conflicting options
                    if (existingOptions != options)
                    {
                        var propertyPath = $"{property.DeclaringType.ClrType.Name}.{property.Name}";
                        throw new InvalidOperationException(
                            $"Conflicting sequence options detected for sequence '{sequenceName}' in scope '{sequenceScope}' " +
                            $"(bucket '{sequenceBucket}'). Property '{propertyPath}' specifies different options than a " +
                            $"previously configured property. Existing: {existingOptions.ToSqlOptionsClause()}, " +
                            $"Conflicting: {options.ToSqlOptionsClause()}. Ensure all properties using the same sequence " +
                            "have identical options.");
                    }
                }
                else
                {
                    sequences[key] = options;
                }
            }
        }

        // Create each sequence
        foreach (var ((bucket, scope, name), options) in sequences)
        {
            await CreateSequenceAsync(bucket, scope, name, options, cancellationToken);
        }
    }

    private async Task CreateSequenceAsync(
        string bucketName, string scope, string sequenceName, CouchbaseSequenceOptions options, CancellationToken cancellationToken)
    {
        var bucket = await GetBucketAsync(bucketName, cancellationToken);
        var scopeObj = await bucket.ScopeAsync(scope);

        // Build CREATE SEQUENCE statement using proper identifier escaping
        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(bucketName);
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
        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Bucket);
        var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Scope);
        var collectionIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Collection);

        var sql = $"CREATE PRIMARY INDEX IF NOT EXISTS ON {bucketIdentifier}.{scopeIdentifier}.{collectionIdentifier}";

        await ExecuteDdlWithRetryAsync(keyspace, sql, cancellationToken);
    }

    /// <summary>
    /// Creates a secondary index for every <c>HasIndex()</c> declared on the model, when
    /// <see cref="ICouchbaseDbContextOptionsBuilder.AutoCreateIndexes"/> is enabled, and waits for
    /// each to report online before returning. Reuses the same "collection was never created"
    /// skip rule as <see cref="CreateIndexesAsync"/> (primary indexes).
    /// </summary>
    /// <remarks>
    /// Only indexes declared directly on a non-owned entity type's own properties are supported —
    /// an index referencing a property declared on an owned type is not resolvable to a single
    /// JSON field path in this pass and is skipped with a warning (see
    /// <see cref="TryResolveIndexFieldNames"/>). N1QL's GSI has no concept of a unique constraint
    /// (an index doesn't reject duplicate values the way a relational unique index does), so
    /// <c>IsUnique</c> is logged as a no-op warning rather than silently ignored or attempted.
    /// </remarks>
    private async Task CreateSecondaryIndexesAsync(CancellationToken cancellationToken)
    {
        if (!_couchbaseDbContextOptionsBuilder.AutoCreateIndexes)
        {
            _logger.LogDebug("Skipping auto-creation of secondary indexes (AutoCreateIndexes = false)");
            return;
        }

        var configuredScope = _couchbaseDbContextOptionsBuilder.Scope;
        // Keyed by (keyspace, index name) rather than a List: a TPH-shared collection (multiple
        // entity types mapped to the same collection) can otherwise yield the same index more than
        // once. CREATE INDEX ... IF NOT EXISTS is idempotent server-side regardless, but this also
        // avoids waiting for the same index to come online twice.
        var indexesToCreate = new Dictionary<(CouchbaseKeyspace Keyspace, string Name), string>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            var keyspace = ResolveEntityKeyspace(tableName);

            // A collection that CreateCollectionsAsync skipped (non-default scope with
            // AutoCreateScopes disabled) was never created, so there is nothing to index.
            if (keyspace.Scope != configuredScope && !_couchbaseDbContextOptionsBuilder.AutoCreateScopes)
            {
                continue;
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (string.IsNullOrEmpty(indexName))
                {
                    // Unlike CREATE PRIMARY INDEX (which is anonymous), N1QL requires every
                    // secondary index to have a name -- GetDatabaseName() can still return null
                    // (e.g. the entity's table name itself couldn't be resolved), so this must be
                    // treated as "cannot create," not assumed non-null.
                    _logger.LogWarning(
                        "An index on entity '{EntityName}' has no database name and will not be " +
                        "auto-created — Couchbase secondary (GSI) indexes require an explicit name " +
                        "(unlike CREATE PRIMARY INDEX, which is anonymous). Call HasDatabaseName(...) " +
                        "on the index, or create it manually.",
                        entityType.ClrType.Name);
                    continue;
                }

                if (index.IsUnique)
                {
                    _logger.LogWarning(
                        "Index '{IndexName}' on entity '{EntityName}' is configured as unique, but Couchbase " +
                        "secondary (GSI) indexes cannot enforce uniqueness — it will be created as a plain, " +
                        "non-unique index. Enforce uniqueness in application code if required.",
                        indexName, entityType.ClrType.Name);
                }

                if (!TryResolveIndexFieldNames(index, entityType, out var fieldNames))
                {
                    continue;
                }

                var ddl = BuildCreateIndexDdl(keyspace, indexName, fieldNames, index.GetFilter());

                // A conflict here (same keyspace + name, different DDL) means two distinct index
                // definitions in the model collide on name. That's a real model bug, not something
                // safe to silently pick one of: "CREATE INDEX ... IF NOT EXISTS" is a no-op for
                // whichever definition loses the race, so the actually-created index could
                // permanently diverge from one of the two definitions without any error ever
                // surfacing. Fail loudly instead.
                if (indexesToCreate.TryGetValue((keyspace, indexName), out var existingDdl))
                {
                    if (existingDdl != ddl)
                    {
                        throw new InvalidOperationException(
                            $"Two different index definitions are both named '{indexName}' for " +
                            $"{keyspace.ToSqlString()}, but describe different DDL. Secondary index " +
                            "names must be unique and unambiguous within a keyspace, since " +
                            "\"CREATE INDEX ... IF NOT EXISTS\" silently keeps whichever definition is " +
                            $"created first. Existing: {existingDdl} New: {ddl}. Rename one of the " +
                            "conflicting indexes.");
                    }
                }
                else
                {
                    indexesToCreate[(keyspace, indexName)] = ddl;
                }
            }
        }

        foreach (var ((keyspace, _), ddl) in indexesToCreate)
        {
            await ExecuteDdlWithRetryAsync(keyspace, ddl, cancellationToken);
        }

        foreach (var (keyspace, indexName) in indexesToCreate.Keys)
        {
            await WaitForSecondaryIndexOnlineAsync(keyspace, indexName, cancellationToken);
        }
    }

    /// <summary>
    /// Resolves an index's properties to JSON field names via <see cref="IReadOnlyProperty.GetColumnName()"/>
    /// (verbatim — root-level entity properties are unaffected by <c>FieldNamingPolicy</c>, which
    /// only applies to owned-type nested JSON; confirmed against <c>CouchbaseDatabaseWrapper</c>'s
    /// own write path). Returns <see langword="false"/> (logging a warning) if any property in the
    /// index is declared on an owned type, since that isn't resolvable to a single JSON field path
    /// on the root document in this pass.
    /// </summary>
    private bool TryResolveIndexFieldNames(IIndex index, IEntityType entityType, out List<string> fieldNames)
    {
        fieldNames = new List<string>(index.Properties.Count);

        foreach (var property in index.Properties)
        {
            if (property.DeclaringType is IReadOnlyEntityType declaringEntityType && declaringEntityType.IsOwned())
            {
                _logger.LogWarning(
                    "Index '{IndexName}' on entity '{EntityName}' references property '{PropertyName}' " +
                    "declared on an owned type and will not be auto-created — only indexes on the " +
                    "entity's own direct properties are supported. Create this index manually.",
                    index.GetDatabaseName(), entityType.ClrType.Name, property.Name);
                return false;
            }

            fieldNames.Add(property.GetColumnName());
        }

        return true;
    }

    private string BuildCreateIndexDdl(CouchbaseKeyspace keyspace, string indexName, List<string> fieldNames, string? filter)
    {
        var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Bucket);
        var scopeIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Scope);
        var collectionIdentifier = _sqlGenerationHelper.DelimitIdentifier(keyspace.Collection);
        var indexIdentifier = _sqlGenerationHelper.DelimitIdentifier(indexName);
        var fieldList = string.Join(", ", fieldNames.Select(_sqlGenerationHelper.DelimitIdentifier));

        var sql = $"CREATE INDEX {indexIdentifier} IF NOT EXISTS ON {bucketIdentifier}.{scopeIdentifier}.{collectionIdentifier}({fieldList})";

        // EF Core's HasFilter() takes a raw provider-specific SQL predicate string that the user
        // writes directly (the same convention SqlServer/Sqlite treat it under) — spliced verbatim
        // into the WHERE clause rather than translated, matching that established pattern.
        if (!string.IsNullOrEmpty(filter))
        {
            sql += $" WHERE {filter}";
        }

        return sql;
    }

    /// <summary>
    /// Executes a DDL statement (index creation, etc.) against <paramref name="keyspace"/>'s scope,
    /// retrying on failure. A collection just created by <see cref="CreateCollectionsAsync"/> may
    /// not be visible to the query service yet — the management API's <c>CreateCollectionAsync</c>
    /// can return before the query service's metadata cache picks up the new keyspace, so the very
    /// next statement can fail with "Keyspace not found" even though the collection genuinely
    /// exists. Retry rather than fail <c>EnsureCreatedAsync</c> over what is normally a sub-second
    /// propagation delay. Shared by primary- and secondary-index creation.
    /// </summary>
    private async Task ExecuteDdlWithRetryAsync(CouchbaseKeyspace keyspace, string sql, CancellationToken cancellationToken)
    {
        var bucket = await GetBucketAsync(keyspace.Bucket, cancellationToken);
        var scopeObj = await bucket.ScopeAsync(keyspace.Scope);

        _logger.LogDebug("Executing DDL for {Keyspace}: {Sql}", keyspace.ToSqlString(), sql);

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
                    "DDL execution for {Keyspace} failed (attempt {Attempt}/{MaxAttempts}); retrying...",
                    keyspace.ToSqlString(), attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
            }
        }
    }

    private async Task WaitForIndexOnlineAsync(CouchbaseKeyspace keyspace, CancellationToken cancellationToken)
    {
        // Per-keyspace deadline: a shared deadline would let time spent waiting on earlier
        // keyspaces eat into the budget for later ones, causing spurious timeouts.
        var onlineDeadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(60);
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

            if (_timeProvider.GetUtcNow() > onlineDeadline)
            {
                throw new TimeoutException(
                    $"Primary index for {keyspace.ToSqlString()} did not come online within 60 seconds.", lastError);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
        }
    }

    private async Task WaitForSecondaryIndexOnlineAsync(CouchbaseKeyspace keyspace, string indexName, CancellationToken cancellationToken)
    {
        var onlineDeadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(60);
        Exception? lastError = null;

        while (true)
        {
            var online = false;
            try
            {
                // Unlike primary indexes (which explicitly report is_primary = true), a secondary
                // index's system:indexes row was empirically observed (live spike) to OMIT the
                // is_primary field entirely rather than set it to false. Filtering on
                // "is_primary = false" therefore never matches -- N1QL treats a comparison against
                // a missing field as missing, which WHERE treats as falsy -- so that clause is
                // deliberately left out here; matching by name within this specific keyspace is
                // already unambiguous.
                using var result = await _cluster.QueryAsync<int>(
                    "SELECT RAW COUNT(*) FROM system:indexes WHERE state = 'online' "
                    + "AND bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection AND name = $name",
                    new QueryOptions()
                        .Parameter("bucket", keyspace.Bucket)
                        .Parameter("scope", keyspace.Scope)
                        .Parameter("collection", keyspace.Collection)
                        .Parameter("name", indexName)
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
                lastError = ex;
            }

            if (online)
            {
                _logger.LogDebug("Secondary index '{IndexName}' online for {Keyspace}", indexName, keyspace.ToSqlString());
                return;
            }

            if (_timeProvider.GetUtcNow() > onlineDeadline)
            {
                throw new TimeoutException(
                    $"Secondary index '{indexName}' for {keyspace.ToSqlString()} did not come online within 60 seconds.",
                    lastError);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
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
        // Collect all unique sequences from the model, keyed by (bucket, scope, name) -- see
        // CreateSequencesAsync's comment on why bucket must be part of the key.
        var sequences = new HashSet<(string Bucket, string Scope, string Name)>();

        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            var sequenceBucket = CouchbaseKeyspace.ResolveBucket(
                entityType.GetTableName(), _couchbaseDbContextOptionsBuilder.Bucket);

            foreach (var property in entityType.GetProperties())
            {
                var sequenceName = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value as string;
                if (string.IsNullOrEmpty(sequenceName))
                {
                    continue;
                }

                var sequenceScope = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value as string
                    ?? _couchbaseDbContextOptionsBuilder.Scope;

                sequences.Add((sequenceBucket, sequenceScope, sequenceName));
            }
        }

        // Drop each sequence
        foreach (var (bucketName, scope, sequenceName) in sequences)
        {
            try
            {
                var bucket = await GetBucketAsync(bucketName, cancellationToken);
                var scopeObj = await bucket.ScopeAsync(scope);

                // Use proper identifier escaping
                var bucketIdentifier = _sqlGenerationHelper.DelimitIdentifier(bucketName);
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
                _logger.LogWarning(ex, "Failed to drop sequence {SequenceName} in scope {Scope} (bucket {BucketName})",
                    sequenceName, scope, bucketName);
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
    /// regardless of whether the bucket already existed. A primary index, and a secondary index
    /// for every <c>HasIndex()</c> declared on the model, are additionally created — and waited
    /// for online — when <see cref="ICouchbaseDbContextOptionsBuilder.AutoCreateIndexes"/> is
    /// enabled.
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
        // ensures the required scopes per bucket before creating collections. CreateIndexesAsync/
        // CreateSecondaryIndexesAsync run last since they need the collections they index to
        // already exist, and are both no-ops unless AutoCreateIndexes is enabled.
        await CreateCollectionsAsync(cancellationToken);
        await CreateSequencesAsync(cancellationToken);
        await CreateIndexesAsync(cancellationToken);
        await CreateSecondaryIndexesAsync(cancellationToken);

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
