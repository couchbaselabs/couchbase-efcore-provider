using System.Diagnostics;
using Couchbase.Diagnostics;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
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
    private ICluster _cluster;

    public CouchbaseDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies,
        IDatabase database,
        IServiceProvider serviceProvider,
        IDesignTimeModel designTimeModel,
        ILogger<CouchbaseDatabaseCreator> logger,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder) : base(dependencies)
    {
        _database = database;
        _designTimeModel = designTimeModel;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var clusterProvider = _serviceProvider.GetRequiredKeyedService<IClusterProvider>(_couchbaseDbContextOptionsBuilder.ClusterOptions.ConnectionString);
        _cluster = await clusterProvider.GetClusterAsync();
    }

    public override bool Exists()
    {
        InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var bucketManager = _cluster.Buckets;
        try
        {
            bucketManager.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return false;
        }

        return true;
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

    private bool ScopeExists()
    {
        var exists = false;
        var manager = GetBucketAsync().GetAwaiter().GetResult().Collections;
        try
        {
            var scopes = manager.GetAllScopesAsync().GetAwaiter().GetResult();
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

    private bool CollectionsExists()
    {
        var manager = GetBucketAsync().GetAwaiter().GetResult().Collections;
        var scopes = manager.GetAllScopesAsync().GetAwaiter().GetResult();

        try
        {
            var scope = scopes.FirstOrDefault(x => x.Name == _couchbaseDbContextOptionsBuilder.Scope);
            var entityTypes = _designTimeModel.Model.GetEntityTypes();
            foreach (var entityType in entityTypes)
            {
                if (scope!.Collections.Contains(new CollectionSpec(scope.Name, entityType.Name)))
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

    public override void Create()
    {
        InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var manager = _cluster.Buckets;
        manager.CreateBucketAsync(new BucketSettings
        {
            Name = _couchbaseDbContextOptionsBuilder.Bucket,
            BucketType = BucketType.Couchbase
        }).GetAwaiter().GetResult();

        do
        {
            Thread.Sleep(1000);
        }while(!Exists());
    }

    private void CreateScope()
    {
        var manager = GetBucketAsync().GetAwaiter().GetResult().Collections;
        var scopes = manager.GetAllScopesAsync().GetAwaiter().GetResult();
        if(!scopes.Contains(new ScopeSpec(_couchbaseDbContextOptionsBuilder.Bucket)))
        {
            try
            {
                manager.CreateScopeAsync(_couchbaseDbContextOptionsBuilder.Scope).GetAwaiter().GetResult();
            }
            catch (ScopeExistsException)
            {
                _logger.LogWarning("Couchbase scope already exists.");
            }
        }
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

    private void CreateCollections()
    {
        var manager = GetBucketAsync().GetAwaiter().GetResult().Collections;
        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            try
            {
                manager.CreateCollectionAsync(_couchbaseDbContextOptionsBuilder.Scope, 
                    entityType.Name, new CreateCollectionSettings())
                    .GetAwaiter().GetResult();
            }
            catch (CollectionExistsException)
            {
               _logger.LogWarning("Couchbase collection already exists.");
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

    public override void Delete()
    {
        InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var manager = _cluster.Buckets;
        try
        {
            manager.DropBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).GetAwaiter().GetResult();
        }
        catch (BucketNotFoundException)
        {
            _logger.LogWarning("Couchbase bucket was not found.");
        }
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
            return true;
        }

        return false;
    }
}