using System.Collections;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>
{
    private readonly RelationalQueryContext _relationalQueryContext;
    private readonly RelationalCommandCache _relationalCommandCache;
    private readonly DbContext _dbContext;
    private readonly bool _standAloneStateManager;
    private readonly IClusterProvider _clusterProvider;

    public CouchbaseQueryEnumerable(
        RelationalQueryContext relationalQueryContext, 
        RelationalCommandCache relationalCommandCache,
        bool standAloneStateManager,
        IClusterProvider _clusterProvider)
    {

        _dbContext = relationalQueryContext.Context;
        _standAloneStateManager = standAloneStateManager;
        this._clusterProvider = _clusterProvider;
        _relationalQueryContext = relationalQueryContext;
        _relationalCommandCache = relationalCommandCache;
    }
    
    public IEnumerator<T> GetEnumerator()
    {
        var queryOptions = new QueryOptions();
        foreach (var parameter in _relationalQueryContext.ParameterValues)
        {
            queryOptions.Parameter(parameter.Key, parameter.Value);
        }
        
        var cluster = _clusterProvider.GetClusterAsync().GetAwaiter().GetResult();
        var command = _relationalCommandCache.RentAndPopulateRelationalCommand(_relationalQueryContext);
        var result = cluster.QueryAsync<T>(command.CommandText, queryOptions).GetAwaiter().GetResult();

        _relationalQueryContext.InitializeStateManager(_standAloneStateManager);

        var model = _dbContext.Model;
        var entityType = model.FindEntityType(typeof(T));

        foreach (var doc in result.ToEnumerable())
        {
            try
            {
                //If the returned type is an entity add start change tracking
                //Scalar values for functions like COUNT are not tracked.
                if (entityType != null)
                {
                    _relationalQueryContext.StartTracking(entityType, doc, new ValueBuffer());
                }
            }
            catch (Exception e)
            {
                //log error
            }

            yield return doc;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
    {
        var command = _relationalCommandCache.RentAndPopulateRelationalCommand(_relationalQueryContext);
        var queryOptions = GetParameters(command);
        
        var cluster = await _clusterProvider.GetClusterAsync().ConfigureAwait(false);
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
                //log error
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
}