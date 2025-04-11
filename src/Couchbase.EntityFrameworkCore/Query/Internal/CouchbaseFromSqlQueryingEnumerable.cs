using System.Collections;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseFromSqlQueryingEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T> , IRelationalQueryingEnumerable
{
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly RelationalCommandCache _relationalCommandCache;
    private readonly DbContext _dbContext;
    private readonly bool _standAloneStateManager;
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;

    public CouchbaseFromSqlQueryingEnumerable(
        RelationalQueryContext relationalQueryContext, 
        RelationalCommandCache relationalCommandCache,
        bool standAloneStateManager,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {

        _dbContext = relationalQueryContext.Context;
        _standAloneStateManager = standAloneStateManager;
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _relationalQueryContext = relationalQueryContext;
        _relationalCommandCache = relationalCommandCache;
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
        var command = _relationalCommandCache.RentAndPopulateRelationalCommand(_relationalQueryContext);
        var logger = (CouchbaseRelationalDiagnosticsCommandLogger)_relationalQueryContext.CommandLogger;
#if DEBUG
        //This likely needs to be refactored and just use the relational command instead
        var loggingCommand = CreateDbCommand();
        logger.LogStatement(loggingCommand, TimeSpan.Zero);
#endif
        var queryOptions = GetParameters(command);

        var bucket = await _bucketProvider.
            GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).
            ConfigureAwait(false);
        var cluster = bucket.Cluster;
        var result = await cluster.QueryAsync<T>(command.CommandText, queryOptions).ConfigureAwait(false);

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
                    _relationalQueryContext.StartTracking(entityType, doc, new ValueBuffer());
                }
            }
            catch (Exception e)
            {
                logger.Logger.LogError("{E}", e);
            }

            yield return doc;
        }
    }
    
    private QueryOptions GetParameters(IRelationalCommand command)
    {
        var queryOptions = new QueryOptions();
        foreach (var parameter in _relationalQueryContext.ParameterValues)
        {
            var key = parameter.Key;
            var value = parameter.Value;

            foreach (var compositeParameter in command.Parameters)
            {
                if (compositeParameter is CompositeRelationalParameter actualParameter)
                {
                    if (actualParameter.InvariantName == key)
                    {
                        var count = 0;
                        var values = value as object[];
                        foreach (var relationalParameter in actualParameter.RelationalParameters)
                        {
                            if (relationalParameter is TypeMappedRelationalParameter typeMappedRelationalParameter)
                            {
                                key = typeMappedRelationalParameter.Name;
                                queryOptions.Parameter(key, values[count++]);
                            }
                        }
                    }
                }
                else
                {
                    queryOptions.Parameter(key, UnWrap(value));
                }
            }
        }
        return queryOptions;
    }

    private object? UnWrap(object? value)
    {
        var type = value?.GetType();
        if (type is { IsArray: true })
        {
            if (value is object[] outValue)
            {
                return outValue.FirstOrDefault();
            }

            throw new ArgumentException(@"Cannot parse!", nameof(value));
        }

        return value;
    }

    public string ToQueryString()
    {
        throw new NotImplementedException();
    }

    public DbCommand CreateDbCommand()=> _relationalCommandCache
        .GetRelationalCommandTemplate(_relationalQueryContext.ParameterValues)
        .CreateDbCommand(
            new RelationalCommandParameterObject(
                _relationalQueryContext.Connection,
                _relationalQueryContext.ParameterValues,
                null,
                null,
                null, CommandSource.LinqQuery),
            Guid.Empty,
            (DbCommandMethod)(-1));
}