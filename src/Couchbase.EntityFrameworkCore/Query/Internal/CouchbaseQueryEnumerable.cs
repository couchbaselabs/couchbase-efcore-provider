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
    // True only for QueryTrackingBehavior.TrackAll — the only mode where SnapshotCollectionRefs
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

    public CouchbaseQueryEnumerable(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
        string[]? projectionAliases,
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
        _ownedCollectionNavigations = ownerEntityType == null ? [] :
            ownerEntityType.GetNavigations()
                .Where(n => n.IsCollection && n.TargetEntityType.IsOwned())
                .ToArray();
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
                if (_ownedCollectionNavigations.Count > 0
                    && navRow is JsonElement rowElement
                    && rowElement.ValueKind == JsonValueKind.Object)
                {
                    PopulateCollectionNavigations(result, rowElement, _ownedCollectionNavigations, _couchbaseDbContextOptionsBuilder.FieldNamingPolicy, _couchbaseDbContextOptionsBuilder.SerializerOptions);
                    SnapshotCollectionRefs(result);
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
                    if (_ownedCollectionNavigations.Count > 0
                        && pendingEntityRow is JsonElement lastRow
                        && lastRow.ValueKind == JsonValueKind.Object)
                    {
                        PopulateCollectionNavigations(last, lastRow, _ownedCollectionNavigations, _couchbaseDbContextOptionsBuilder.FieldNamingPolicy, _couchbaseDbContextOptionsBuilder.SerializerOptions);
                        SnapshotCollectionRefs(last);
                    }
                    pendingEntityRow = null;
                    yield return last;
                    // hasNext is false so the outer while exits naturally.
                }
            }
        }
    }

    private static readonly JsonSerializerOptions _defaultSerializerOptions = new(JsonSerializerDefaults.Web);

    private static void PopulateCollectionNavigations(T entity, JsonElement docElement, IReadOnlyList<INavigation> collections, JsonNamingPolicy? fieldNamingPolicy, JsonSerializerOptions? serializerOptions)
    {
        var options = serializerOptions ?? _defaultSerializerOptions;
        foreach (var nav in collections)
        {
            var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;
            if (!docElement.TryGetPropertyCI(fieldName, out var arrayElement)
                || arrayElement.ValueKind != JsonValueKind.Array)
                continue;

            var accessor = nav.GetCollectionAccessor();
            var clrType = nav.TargetEntityType.ClrType;
            var properties = OwnedCollectionSnapshot.GetTrackedProperties(nav);

            if (accessor != null)
            {
                // Clear any items the EF Core shaper may have pre-populated from the injected
                // OwnsMany column (observed with AsNoTracking queries). We are the authoritative
                // source for owned-collection data; clearing before adding prevents duplicates.
                var coll = accessor.GetOrCreate(entity!, forMaterialization: true);
                (coll as IList)?.Clear();
            }
            else
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(clrType))!;
                nav.PropertyInfo?.SetValue(entity, list);
            }

            foreach (var itemElement in arrayElement.EnumerateArray())
            {
                var ownedEntity = Activator.CreateInstance(clrType)!;
                foreach (var prop in properties)
                {
                    var jsonKey = fieldNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
                    if (itemElement.TryGetPropertyCI(jsonKey, out var propElement))
                        prop.PropertyInfo?.SetValue(ownedEntity, ConvertJsonValue(propElement, prop.ClrType, options));
                }
                if (accessor != null)
                    accessor.Add(entity!, ownedEntity, forMaterialization: true);
                else
                    ((IList)nav.PropertyInfo!.GetValue(entity)!).Add(ownedEntity);
            }
        }
    }

    // Record the original collection-object reference and per-item property values for every
    // OwnsMany navigation on the freshly materialised entity.
    //
    // OriginalRefs detects reference replacement (customer.ContactMethods = []).
    // OriginalItems detects in-place changes: .Add(), .Remove(), or scalar property mutation.
    // Both are checked in MarkOwnersWithReplacedCollections before SaveChanges.
    private void SnapshotCollectionRefs(T? entity)
    {
        // Skip entirely for non-tracking queries: the interceptor walks ChangeTracker.Entries(),
        // so snapshots built for NoTracking / NoTrackingWithIdentityResolution entities are
        // never consumed and would be immediately GC'd.
        if (entity == null || !_isTracking) return;
        var refs  = OwnedCollectionSnapshot.OriginalRefs.GetOrCreateValue(entity);
        var items = OwnedCollectionSnapshot.OriginalItems.GetOrCreateValue(entity);
        foreach (var nav in _ownedCollectionNavigations)
        {
            var currentCollection = nav.PropertyInfo?.GetValue(entity);
            refs[nav.Name] = currentCollection;

            // Snapshot per-item property values so in-place mutations can be detected even
            // when the list reference is unchanged.
            if (currentCollection is IEnumerable collection)
            {
                var itemProps = OwnedCollectionSnapshot.GetTrackedProperties(nav);
                var snapshot = new List<Dictionary<string, object?>>();
                foreach (var item in collection)
                {
                    if (item == null) continue;
                    var propSnapshot = new Dictionary<string, object?>();
                    foreach (var prop in itemProps)
                    {
                        var raw = prop.PropertyInfo?.GetValue(item);
                        // Use EF Core's ValueComparer.Snapshot so mutable reference types
                        // (e.g. byte[]) are deep-copied. For immutable types (string, int, …)
                        // Snapshot is a no-op that returns the same reference.
                        propSnapshot[prop.Name] = raw is null ? null : OwnedCollectionSnapshot.GetComparer(prop).Snapshot(raw);
                    }
                    snapshot.Add(propSnapshot);
                }
                items[nav.Name] = snapshot;
            }
            else
            {
                items[nav.Name] = [];
            }
        }
    }


    private static object? ConvertJsonValue(JsonElement element, Type targetType, JsonSerializerOptions options)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return t switch
        {
            _ when t == typeof(string)   => element.GetString(),
            _ when t == typeof(int)      => element.GetInt32(),
            _ when t == typeof(long)     => element.GetInt64(),
            _ when t == typeof(double)   => element.GetDouble(),
            _ when t == typeof(decimal)  => element.GetDecimal(),
            _ when t == typeof(float)    => (float)element.GetDouble(),
            _ when t == typeof(bool)     => element.GetBoolean(),
            _ when t == typeof(Guid)     => element.GetGuid(),
            _ when t == typeof(DateTime) => element.GetDateTime(),
            _ => JsonSerializer.Deserialize(element.GetRawText(), t, options)
        };
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
        foreach (CouchbaseParameter parameter in command.Parameters)
        {
            queryOptions.Parameter(parameter.ParameterName, parameter.Value);
        }

        return queryOptions;
    }
}
