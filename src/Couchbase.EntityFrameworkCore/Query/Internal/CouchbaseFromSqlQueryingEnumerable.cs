using System.Collections;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public static class CouchbaseFromSqlQueryingEnumerable
{
    public static CouchbaseFromSqlQueryingEnumerable<T> Create<T>(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
        IReadOnlyList<string> columnNames,
        Func<QueryContext, DbDataReader, int[], T> shaper,
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
            columnNames,
            shaper,
            contextType,
            standAloneStateManager,
            detailedErrorsEnabled,
            threadSafetyChecksEnabled,
            bucketProvider,
            couchbaseDbContextOptionsBuilder);
}


public class CouchbaseFromSqlQueryingEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T> , IRelationalQueryingEnumerable
{
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly RelationalCommandCache _relationalCommandCache;
    private readonly DbContext _dbContext;
    private readonly bool _standAloneStateManager;
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly RelationalCommandResolver _relationalCommandResolver;
    private readonly IReadOnlyList<ReaderColumn?>? _readerColumns;
    private readonly IReadOnlyList<string> _columnNames;
    private readonly Func<QueryContext, DbDataReader, int[], T> _shaper;
    private readonly Type _contextType;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _queryLogger;
    private readonly bool _detailedErrorsEnabled;
    private readonly bool _threadSafetyChecksEnabled;

    public CouchbaseFromSqlQueryingEnumerable(
        RelationalQueryContext relationalQueryContext,
        RelationalCommandResolver relationalCommandResolver,
        IReadOnlyList<ReaderColumn?>? readerColumns,
        IReadOnlyList<string> columnNames,
        Func<QueryContext, DbDataReader, int[], T> shaper,
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
        _columnNames = columnNames;
        _shaper = shaper;
        _contextType = contextType;
        _queryLogger = relationalQueryContext.QueryLogger;
        _standAloneStateManager = standAloneStateManager;
        _detailedErrorsEnabled = detailedErrorsEnabled;
        _threadSafetyChecksEnabled = threadSafetyChecksEnabled;
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _dbContext = relationalQueryContext.Context;
    }
    
    public IEnumerator<T> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
    {
        using var dbCommand = CreateDbCommand();
        // Use CommandText directly — RelationalQueryStringFactory.Create() would prepend
        // per-parameter comment blocks before the SQL, which must not be sent to the server.
        // Using CommandText keeps the executed SQL identical to what ToQueryString() returns.
        var queryString = dbCommand.CommandText;

        var logger = (CouchbaseRelationalDiagnosticsCommandLogger)_relationalQueryContext.CommandLogger;
#if DEBUG
        logger.LogStatement(dbCommand, TimeSpan.Zero);
#endif
        var queryOptions = GetParameters(dbCommand);

        var bucket = await _bucketProvider.
            GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).
            ConfigureAwait(false);
        var cluster = bucket.Cluster;
        var result = await cluster.QueryAsync<T>(queryString, queryOptions).ConfigureAwait(false);

        _relationalQueryContext.InitializeStateManager(_standAloneStateManager);

        var model = _dbContext.Model;
        var entityType = model.FindEntityType(typeof(T));

        await foreach (var doc in result)
        {
            try
            {
                //If the returned type is an entity add start change tracking
                //Scalar values for functions like COUNT are not tracked.
                if (entityType != null && _dbContext.Entry(doc).State != EntityState.Detached)
                {
                    _relationalQueryContext.StartTracking(entityType, doc, Snapshot.Empty);
                }
            }
            catch (Exception e)
            {
                logger.Logger.LogError("{E}", e);
            }

            yield return doc;
        }
    }
    
    private QueryOptions GetParameters(DbCommand command)
    {
        var queryOptions = new QueryOptions();
        foreach (CouchbaseParameter parameter in command.Parameters)
        {
            queryOptions.Parameter(parameter.ParameterName, parameter.Value);
        }

        return queryOptions;
    }

    public string ToQueryString()
    {
        // Compile the SQL without opening a database connection.  This matches
        // EF Core's contract for ToQueryString() and the implementation in
        // CouchbaseQueryEnumerable.ToQueryString().
        var relationalCommand = _relationalCommandResolver(_relationalQueryContext.Parameters);
        return relationalCommand.CommandText;
    }

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
}