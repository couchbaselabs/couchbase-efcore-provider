using System.Collections;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>, IRelationalQueryingEnumerable
{
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly RelationalCommandCache _relationalCommandCache;
    private readonly DbContext _dbContext;
    private readonly bool _standAloneStateManager;
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;

    public CouchbaseQueryEnumerable(
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
        throw ExceptionHelper.SyncroIONotSupportedException();
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
                        foreach (var relationalParameter in actualParameter.RelationalParameters)
                        {
                            if (relationalParameter is TypeMappedRelationalParameter typeMappedRelationalParameter)
                            {
                                key = typeMappedRelationalParameter.Name;
                            }
                        }
                    }
                }
            }
            queryOptions.Parameter(key, UnWrap(value));
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
        throw ExceptionHelper.SyncroIONotSupportedException();
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
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
