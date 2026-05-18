// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Data.Common;
using System.Text.Json;
using Couchbase.Query;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Storage.Internal;
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
        var queryString = _relationalQueryContext.RelationalQueryStringFactory.Create(dbCommand);
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
                    }
                    pendingEntityRow = null;
                    yield return last;
                    // hasNext is false so the outer while exits naturally.
                }
            }
        }
    }

    // KNOWN LIMITATION — change-tracker bypass
    // Owned collection items created here are materialized via Activator.CreateInstance and
    // assigned directly to the navigation property via reflection. They are NOT registered with
    // the EF Core state manager, which means:
    //   • Modifications to the items after the query (e.g. customer.ContactMethods[0].Value = "x")
    //     will NOT be detected by SaveChanges, regardless of whether the query used tracking or
    //     no-tracking — the behavior is silently identical in both cases.
    //   • This deviates from standard EF Core semantics, where owned collection members
    //     materialized through the shaper are tracked alongside their owner.
    // Fixing this requires hooking each owned item into IStateManager via the owner's
    // InternalEntityEntry, which is deferred as a follow-up task.
    private static readonly JsonSerializerOptions _defaultSerializerOptions = new(JsonSerializerDefaults.Web);

    private static void PopulateCollectionNavigations(T entity, JsonElement docElement, IReadOnlyList<INavigation> collections, JsonNamingPolicy? fieldNamingPolicy, JsonSerializerOptions? serializerOptions)
    {
        var options = serializerOptions ?? _defaultSerializerOptions;
        foreach (var nav in collections)
        {
            var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;
            if (!TryGetPropertyCI(docElement, fieldName, out var arrayElement)
                || arrayElement.ValueKind != JsonValueKind.Array)
                continue;

            var clrType = nav.TargetEntityType.ClrType;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(clrType))!;
            var properties = nav.TargetEntityType.GetProperties()
                .Where(p => !p.IsShadowProperty()).ToList();

            foreach (var itemElement in arrayElement.EnumerateArray())
            {
                var ownedEntity = Activator.CreateInstance(clrType)!;
                foreach (var prop in properties)
                {
                    // Use the serializer's PropertyNamingPolicy to derive the JSON key — the SDK
                    // wrote the field using the CLR property name run through that policy (e.g.
                    // camelCase by default). GetColumnName() returns the EF Core relational column
                    // name (prefixed, PascalCase) which does not match the stored JSON key.
                    var jsonKey = options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
                    if (TryGetPropertyCI(itemElement, jsonKey, out var propElement))
                        prop.PropertyInfo?.SetValue(ownedEntity, ConvertJsonValue(propElement, prop.ClrType, options));
                }
                list.Add(ownedEntity);
            }

            nav.PropertyInfo?.SetValue(entity, list);
        }
    }

    // JsonElement.TryGetProperty is case-sensitive. The SDK's SystemTextJsonSerializer uses
    // JsonSerializerDefaults.Web (camelCase), so navigation/property names may vary in case
    // between what EF Core models use and what appears in the stored document.
    internal static bool TryGetPropertyCI(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var prop in element.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = prop.Value;
            return true;
        }
        value = default;
        return false;
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
    ///         A string representation of the query used.
    ///     </para>
    ///     <para>
    ///         Warning: this string may not be suitable for direct execution is intended only for use in debugging.
    ///     </para>
    /// </summary>
    /// <returns>The query string.</returns>
    public string ToQueryString()
    {
        using var dbCommand = CreateDbCommand();
        return _relationalQueryContext.RelationalQueryStringFactory.Create(dbCommand);
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
