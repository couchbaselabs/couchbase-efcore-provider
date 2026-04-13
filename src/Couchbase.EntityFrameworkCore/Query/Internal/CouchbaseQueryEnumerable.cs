// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Data.Common;
using Couchbase.Query;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public static class CouchbaseQueryEnumerable
{
    public static CouchbaseQueryEnumerable<T> Create<T>(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
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
    private readonly RelationalCommandResolver _relationalCommandResolver;
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly IBucketProvider _bucketProvider;
    private readonly DbContext _dbContext;

    public CouchbaseQueryEnumerable(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
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
        _shaper = shaper;
        _contextType = contextType;
        _queryLogger = relationalQueryContext.QueryLogger;
        _standAloneStateManager = standAloneStateManager;
        _detailedErrorsEnabled = detailedErrorsEnabled;
        _threadSafetyChecksEnabled = threadSafetyChecksEnabled;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _bucketProvider = bucketProvider;
        _dbContext = relationalQueryContext.Context;
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

        var bucket = await _bucketProvider.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).ConfigureAwait(false);
        var cluster = bucket.Cluster;
        var result = await cluster.QueryAsync<T>(queryString, queryOptions).ConfigureAwait(false);

        _relationalQueryContext.InitializeStateManager(_standAloneStateManager);

        var model = _dbContext.Model;
        var entityType = model.FindEntityType(typeof(T));

        await foreach (var doc in result)
        {
            // Scalar values (e.g. COUNT results) are not entities and are never tracked.
            if (entityType == null)
            {
                yield return doc;
                continue;
            }

            var toReturn = doc;
            var primaryKey = entityType.FindPrimaryKey();

            if (primaryKey != null)
            {
                // Extract primary key values from the deserialized document.
                var keyValues = primaryKey.Properties
                    .Select(p => p.PropertyInfo?.GetValue(doc))
                    .ToArray();

                // Identity resolution: if a different instance with the same key is already
                // tracked, return the tracked instance. Returning a second instance with the
                // same key causes EF Core to throw when the caller later calls Remove/Attach.
                var existingEntry = _relationalQueryContext
                    .TryGetEntry(primaryKey, keyValues!, true, out var key);

                if (existingEntry != null)
                {
                    toReturn = (T)existingEntry.Entity;
                }
                else
                {
                    try
                    {
                        _relationalQueryContext.StartTracking(entityType, doc, Snapshot.Empty);
                    }
                    catch (Exception e)
                    {
                        logger.Logger.LogError("{E}", e);
                    }
                }
            }

            yield return toReturn;
        }
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
