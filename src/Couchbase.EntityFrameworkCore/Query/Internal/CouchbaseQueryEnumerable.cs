// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Data.Common;
using System.Text.Json;
using Couchbase.Query;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public static class CouchbaseQueryEnumerable
{
    public static CouchbaseQueryEnumerable<T> Create<T>(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
        string[]? projectionAliases,
        string[]? ownedNavigationKeys,
        string[]? ownedNavigationAliases,
        Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> shaper,
        Type contextType,
        bool standAloneStateManager,
        bool isTracking,
        bool detailedErrorsEnabled,
        bool threadSafetyChecksEnabled,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
        => new(
            relationalQueryContext,
            relationalCommandResolver,
            readerColumns,
            projectionAliases,
            ownedNavigationKeys,
            ownedNavigationAliases,
            shaper,
            contextType,
            standAloneStateManager,
            isTracking,
            detailedErrorsEnabled,
            threadSafetyChecksEnabled,
            bucketProvider,
            couchbaseDbContextOptionsBuilder);
}

public class CouchbaseQueryEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>, IRelationalQueryingEnumerable
{
    private readonly bool _threadSafetyChecksEnabled;
    private readonly bool _detailedErrorsEnabled;
    private readonly bool _standAloneStateManager;
    // True only for QueryTrackingBehavior.TrackAll — the only mode where CouchbaseCollectionSnapshot.Record
    // produces a snapshot that the interceptor can ever consume via ChangeTracker.Entries().
    private readonly bool _isTracking;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _queryLogger;
    private readonly Type _contextType;
    private readonly Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> _shaper;
    private readonly IReadOnlyList<ReaderColumn?>? _readerColumns;
    // Ordered SELECT projection aliases from SelectExpression.Projection — always available and
    // used as the authoritative column-name list for building the reader's ordinal→JSON index map.
    private readonly string[]? _projectionAliases;
    private readonly RelationalCommandResolver _relationalCommandResolver;
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly IBucketProvider _bucketProvider;
    private readonly DbContext _dbContext;
    private readonly IReadOnlyList<INavigation> _ownedCollectionNavigations;
    private readonly bool _ownedCollectionsSpanInheritance;
    private readonly IReadOnlyList<INavigation> _ownedReferenceNavigations;
    private readonly bool _ownedReferencesSpanInheritance;
    // Maps CouchbaseProjectionAliases.NavigationKey(nav) → the navigation's actual (possibly
    // uniquified) N1QL result-row key, resolved at compile time in
    // CouchbaseShapedQueryCompilingExpressionVisitor once ComputeUnique has run. Only root-level
    // owned navigations need this — a field name computed fresh from the naming policy can be
    // stale if it collided with another projected column and was suffixed (e.g. "address0").
    private readonly Dictionary<string, string> _ownedNavigationAliases;
    private readonly CouchbaseOwnedCollectionMaterializer _materializer = new();
    private readonly CouchbaseCollectionSnapshot _snapshot = new();

    public CouchbaseQueryEnumerable(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
        string[]? projectionAliases,
        string[]? ownedNavigationKeys,
        string[]? ownedNavigationAliases,
        Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> shaper,
        Type contextType,
        bool standAloneStateManager,
        bool isTracking,
        bool detailedErrorsEnabled,
        bool threadSafetyChecksEnabled,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _relationalQueryContext = relationalQueryContext;
        _relationalCommandResolver = relationalCommandResolver;
        _readerColumns = readerColumns;
        _projectionAliases = projectionAliases;
        _ownedNavigationAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ownedNavigationKeys != null && ownedNavigationAliases != null)
        {
            for (var i = 0; i < ownedNavigationKeys.Length; i++)
                _ownedNavigationAliases[ownedNavigationKeys[i]] = ownedNavigationAliases[i];
        }
        _shaper = shaper;
        _contextType = contextType;
        _queryLogger = relationalQueryContext.QueryLogger;
        _standAloneStateManager = standAloneStateManager;
        _isTracking = isTracking;
        _detailedErrorsEnabled = detailedErrorsEnabled;
        _threadSafetyChecksEnabled = threadSafetyChecksEnabled;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _bucketProvider = bucketProvider;
        _dbContext = relationalQueryContext.Context;
        // FindEntityType returns null when T is not a mapped entity (scalar projections such as
        // Select(c => c.Name) where T = string, or anonymous projections such as Select(c => new { c.Name })).
        // In both cases _ownedCollectionNavigations is empty and OwnsMany population is skipped.
        var ownerEntityType = relationalQueryContext.Context.Model.FindEntityType(typeof(T));
        // Include OwnsMany navigations declared on derived types (TPH): a base-type query
        // (T = Person) must still populate owned collections on the Student rows it returns.
        // GetNavigations() on the queried type covers inherited + own; GetDeclaredNavigations()
        // on each strict descendant adds the navigations introduced lower in the hierarchy.
        _ownedCollectionNavigations = ownerEntityType == null ? [] :
            ownerEntityType.GetNavigations()
                .Concat(ownerEntityType.GetDerivedTypes().SelectMany(t => t.GetDeclaredNavigations()))
                .Where(n => n.IsCollection && n.TargetEntityType.IsOwned())
                .ToArray();
        // Per-row filtering is only needed when an owned collection is declared on a STRICT
        // DESCENDANT of the queried type — those navigations apply to a subset of the rows.
        // Navigations declared on the queried type or an ancestor (inherited via GetNavigations)
        // apply to every row, so a derived-type query (T = Student) stays a zero-cost
        // pass-through. Test with IsAssignableFrom against the queried type's CLR type.
        _ownedCollectionsSpanInheritance = ownerEntityType != null &&
            _ownedCollectionNavigations.Any(
                n => !n.DeclaringEntityType.ClrType.IsAssignableFrom(ownerEntityType.ClrType));

        // Root-level OwnsOne navigations — additive fallback for genuinely nested JSON objects
        // (see CouchbaseOwnedCollectionMaterializer.PopulateReference). Same TPH-inheritance
        // handling as _ownedCollectionNavigations above.
        _ownedReferenceNavigations = ownerEntityType == null ? [] :
            ownerEntityType.GetNavigations()
                .Concat(ownerEntityType.GetDerivedTypes().SelectMany(t => t.GetDeclaredNavigations()))
                .Where(n => !n.IsCollection && n.TargetEntityType.IsOwned())
                .ToArray();
        _ownedReferencesSpanInheritance = ownerEntityType != null &&
            _ownedReferenceNavigations.Any(
                n => !n.DeclaringEntityType.ClrType.IsAssignableFrom(ownerEntityType.ClrType));
    }

    /// <summary>
    /// Returns the subset of <paramref name="navigations"/> whose declaring entity type is
    /// assignable from <paramref name="entity"/>'s runtime type. For non-inheritance queries
    /// (<paramref name="spansInheritance"/> is <see langword="false"/>) this returns the full
    /// list unchanged. Shared by <see cref="_ownedCollectionNavigations"/> and
    /// <see cref="_ownedReferenceNavigations"/> filtering.
    /// </summary>
    private static IReadOnlyList<INavigation> ApplicableOwnedNavigations(
        IReadOnlyList<INavigation> navigations, bool spansInheritance, object? entity)
    {
        if (!spansInheritance || entity is null)
            return navigations;

        var applicable = new List<INavigation>(navigations.Count);
        foreach (var nav in navigations)
        {
            if (nav.DeclaringEntityType.ClrType.IsInstanceOfType(entity))
                applicable.Add(nav);
        }
        return applicable;
    }

    /// <summary>
    /// If <see cref="_isTracking"/> and any property was overridden by
    /// <see cref="CouchbaseOwnedCollectionMaterializer.PopulateReference{T}"/>, realigns EF Core's
    /// own change-tracking snapshot for each so a later <c>DetectChanges</c> doesn't see the
    /// override as a user-driven mutation and spuriously mark the owner <c>Modified</c>.
    /// <para>
    /// <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry"/> access (via
    /// <see cref="DbContext.Entry"/> or <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry.State"/>)
    /// triggers an implicit <c>DetectChanges()</c> unless <c>AutoDetectChangesEnabled</c> is
    /// disabled first — otherwise that implicit detection would bake in <c>IsModified=true</c>
    /// against the stale original value before this method gets a chance to fix it. This mirrors
    /// the pattern already used by
    /// <see cref="Couchbase.EntityFrameworkCore.Storage.Internal.CouchbaseSaveChangesInterceptor.MarkOwnersWithReplacedCollections"/>.
    /// </para>
    /// </summary>
    private void RealignTrackedOriginalValues(
        IReadOnlyList<CouchbaseOwnedCollectionMaterializer.TouchedProperty> touched)
    {
        if (!_isTracking || touched.Count == 0) return;

        var changeTracker = _dbContext.ChangeTracker;
        var autoDetect = changeTracker.AutoDetectChangesEnabled;
        changeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var t in touched)
            {
                var currentValue = t.Property.PropertyInfo != null
                    ? t.Property.PropertyInfo.GetValue(t.Instance)
                    : t.Property.FieldInfo?.GetValue(t.Instance);
                _dbContext.Entry(t.Instance).Property(t.Property.Name).OriginalValue = currentValue;
            }
        }
        finally
        {
            changeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    /// <returns>An enumerator that can be used to iterate through the collection.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        throw ExceptionHelper.SyncroIONotSupportedException();
    }

    /// <summary>Returns an enumerator that iterates through a collection.</summary>
    /// <returns>An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Returns an enumerator that iterates asynchronously through the collection.</summary>
    /// <param name="cancellationToken">A <see cref="T:System.Threading.CancellationToken" /> that may be used to cancel the asynchronous iteration.</param>
    /// <returns>An enumerator that can be used to iterate asynchronously through the collection.</returns>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
    {
        using var dbCommand = CreateDbCommand();
        // Use CommandText directly — RelationalQueryStringFactory.Create() would prepend
        // per-parameter comment blocks (for debugging) before the SQL, but those comment
        // blocks are not part of the actual query and should not be sent to the server.
        // Using CommandText keeps the executed SQL identical to what ToQueryString() returns.
        var queryString = dbCommand.CommandText;
        var logger = (CouchbaseRelationalDiagnosticsCommandLogger)_relationalQueryContext.CommandLogger;
#if DEBUG
        logger.LogStatement(dbCommand, TimeSpan.Zero);
#endif
        var queryOptions = GetParameters(dbCommand);
        queryOptions.CancellationToken(cancellationToken);

        var bucket = await _bucketProvider.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).ConfigureAwait(false);
        var cluster = bucket.Cluster;

        // Prefer projection aliases (always available from SelectExpression) over readerColumns names.
        // readerColumns can be null in EF Core 10 for certain query shapes.
        var columnNames = _projectionAliases ?? _readerColumns?.Select(rc => rc?.Name).ToArray();
        var queryResult = await cluster.QueryAsync<JsonElement>(queryString, queryOptions).ConfigureAwait(false);
        await using var reader = new CouchbaseDbDataReader<JsonElement>(queryResult, columnNames);

        _relationalQueryContext.InitializeStateManager(_standAloneStateManager);

        var coordinator = new SingleQueryResultCoordinator();

        // pendingEntityRow: the most recent row that belonged to the entity currently being
        // accumulated. The Couchbase SQL generator skips the OwnsMany LEFT JOIN, so each
        // owner document produces exactly one N1QL row. When the shaper sets ResultReady=false
        // (expecting more JOIN rows that never arrive) and the outer loop either reads the
        // next entity's row or hits EOF, CurrentRow has already advanced away from the
        // current entity's row. We therefore save it here before each read.
        JsonElement? pendingEntityRow = null;

        // coordinator.HasNext carries a buffered row signal set by the shaper (collection navigation
        // case: shaper detects a new root key and buffers the current row for the next iteration).
        // Using "??" mirrors EF Core's SingleQueryingEnumerable pattern exactly.
        var hasNext = coordinator.HasNext ?? await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        while (hasNext)
        {
            // Signal to shaper: a row is available and entity materialisation should begin.
            // For simple (non-navigation) queries the shaper leaves ResultReady = true.
            // For collection-navigation queries the shaper sets ResultReady = false while
            // accumulating related rows, and restores it to true when the root entity is complete.
            coordinator.ResultReady = true;
            coordinator.HasNext = null;

            var result = _shaper(_relationalQueryContext, reader, coordinator.ResultContext, coordinator);

            if (coordinator.ResultReady)
            {
                coordinator.ResultContext.Values = null;
                // Use pendingEntityRow if available (entity was accumulated across multiple reads);
                // fall back to CurrentRow for shapers that complete in a single iteration.
                var navRow = pendingEntityRow
                    ?? (reader.CurrentRow is JsonElement r ? r : (JsonElement?)null);
                if ((_ownedCollectionNavigations.Count > 0 || _ownedReferenceNavigations.Count > 0)
                    && navRow is JsonElement rowElement
                    && rowElement.ValueKind == JsonValueKind.Object)
                {
                    var applicableCollections = ApplicableOwnedNavigations(_ownedCollectionNavigations, _ownedCollectionsSpanInheritance, result);
                    if (applicableCollections.Count > 0)
                    {
                        _materializer.Populate(result, rowElement, applicableCollections, _couchbaseDbContextOptionsBuilder.FieldNamingPolicy, _couchbaseDbContextOptionsBuilder.SerializerOptions, _ownedNavigationAliases);
                        _snapshot.Record(result, applicableCollections, _isTracking);
                    }

                    var applicableReferences = ApplicableOwnedNavigations(_ownedReferenceNavigations, _ownedReferencesSpanInheritance, result);
                    if (applicableReferences.Count > 0)
                    {
                        var touched = _materializer.PopulateReference(result, rowElement, applicableReferences, _couchbaseDbContextOptionsBuilder.FieldNamingPolicy, _couchbaseDbContextOptionsBuilder.SerializerOptions, _ownedNavigationAliases);
                        RealignTrackedOriginalValues(touched);
                    }
                }
                pendingEntityRow = null;
                yield return result;
                hasNext = coordinator.HasNext ?? await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (coordinator.HasNext == true)
            {
                // Shaper buffered a row (detected new root key mid-stream); loop without reading.
                hasNext = true;
            }
            else
            {
                // Shaper needs more rows to complete the current entity (collection accumulation).
                // Save the current row before advancing — it belongs to the entity being built.
                if (reader.CurrentRow is JsonElement entityRow)
                    pendingEntityRow = entityRow;

                hasNext = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!hasNext)
                {
                    // EOF: signal the shaper to finalise the last pending root entity.
                    coordinator.HasNext = false;
                    coordinator.ResultReady = true;
                    var last = _shaper(_relationalQueryContext, reader, coordinator.ResultContext, coordinator);
                    coordinator.ResultContext.Values = null;
                    // CurrentRow is null at EOF; use pendingEntityRow saved above.
                    if ((_ownedCollectionNavigations.Count > 0 || _ownedReferenceNavigations.Count > 0)
                        && pendingEntityRow is JsonElement lastRow
                        && lastRow.ValueKind == JsonValueKind.Object)
                    {
                        var applicableCollections = ApplicableOwnedNavigations(_ownedCollectionNavigations, _ownedCollectionsSpanInheritance, last);
                        if (applicableCollections.Count > 0)
                        {
                            _materializer.Populate(last, lastRow, applicableCollections, _couchbaseDbContextOptionsBuilder.FieldNamingPolicy, _couchbaseDbContextOptionsBuilder.SerializerOptions, _ownedNavigationAliases);
                            _snapshot.Record(last, applicableCollections, _isTracking);
                        }

                        var applicableReferences = ApplicableOwnedNavigations(_ownedReferenceNavigations, _ownedReferencesSpanInheritance, last);
                        if (applicableReferences.Count > 0)
                        {
                            var touched = _materializer.PopulateReference(last, lastRow, applicableReferences, _couchbaseDbContextOptionsBuilder.FieldNamingPolicy, _couchbaseDbContextOptionsBuilder.SerializerOptions, _ownedNavigationAliases);
                            RealignTrackedOriginalValues(touched);
                        }
                    }
                    pendingEntityRow = null;
                    yield return last;
                    // hasNext is false so the outer while exits naturally.
                }
            }
        }
    }

    /// <summary>
    ///     <para>
    ///         Returns the SQL++ that would be sent to Couchbase for this query.
    ///     </para>
    ///     <para>
    ///         This method compiles the LINQ expression to SQL++ without opening a database
    ///         connection, so it can be used in unit tests and diagnostic tooling without
    ///         a live Couchbase server.
    ///     </para>
    ///     <para>
    ///         Warning: the returned string may not be suitable for direct execution;
    ///         it is intended only for debugging and logging.
    ///     </para>
    /// </summary>
    /// <returns>The SQL++ query string.</returns>
    public string ToQueryString()
    {
        // Resolve the compiled IRelationalCommand (SQL text + parameter descriptors) without
        // opening a database connection.  Reading CommandText is pure in-memory work —
        // no I/O occurs.  This matches EF Core's documented contract for ToQueryString().
        var relationalCommand = _relationalCommandResolver(_relationalQueryContext.Parameters);
        return relationalCommand.CommandText;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual DbCommand CreateDbCommand()
        => _relationalCommandResolver(_relationalQueryContext.Parameters)
            .CreateDbCommand(
                new RelationalCommandParameterObject(
                    _relationalQueryContext.Connection,
                    _relationalQueryContext.Parameters,
                    null,
                    _relationalQueryContext.Context,
                    null, CommandSource.LinqQuery),
                Guid.Empty,
                (DbCommandMethod)(-1));

    private QueryOptions GetParameters(DbCommand command)
    {
        var queryOptions = new QueryOptions();
        queryOptions.ScanConsistency(_couchbaseDbContextOptionsBuilder.ScanConsistency);
        foreach (CouchbaseParameter parameter in command.Parameters)
        {
            queryOptions.Parameter(parameter.ParameterName, parameter.Value!);
        }

        return queryOptions;
    }
}
