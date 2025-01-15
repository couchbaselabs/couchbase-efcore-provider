using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Google.Api;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseCreator :  RelationalDatabaseCreator
{
    private readonly IDesignTimeModel _designTimeModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private IBucket _bucket;

    public CouchbaseDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies, ICouchbaseClientWrapper couchbaseClientWrapper, IDatabase database, IServiceProvider serviceProvider, IDesignTimeModel designTimeModel, ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder) : base(dependencies)
    {
        _designTimeModel = designTimeModel;
        _serviceProvider = serviceProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }

    private Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var bucketProvider = _serviceProvider.GetRequiredKeyedService<IBucketProvider>(_couchbaseDbContextOptionsBuilder.ConnectionString);
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        _bucket ??= bucketProvider.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public override bool Exists()
    {
        InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var manager = _bucket.Cluster.Buckets;

        try
        {
            manager.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return false;
        }
        
        return true;
    }

    private bool ScopeExists()
    {
        var exists = false;
        var manager = _bucket.Collections;
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

    private bool CollectionsExists()
    {
        var manager = _bucket.Collections;
        var scopes = manager.GetAllScopesAsync().GetAwaiter().GetResult();
        var scope = scopes.FirstOrDefault(x => x.Name == _couchbaseDbContextOptionsBuilder.Scope);
        var entityTypes = _designTimeModel.Model.GetEntityTypes();
        foreach (var entityType in entityTypes)
        {
            if (!scope!.Collections.Contains(new CollectionSpec(scope.Name, entityType.Name)))
            {
                return false;
            }
        }
        return true;
    }

    public override bool HasTables()
    {
        return true;
    }

    public override void Create()
    {
        InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var manager = _bucket.Cluster.Buckets;
        manager.CreateBucketAsync(new BucketSettings
        {
            Name = _couchbaseDbContextOptionsBuilder.Bucket,
            BucketType = BucketType.Couchbase
        }).GetAwaiter().GetResult();

        do
        {
            Thread.Sleep(1000);
        }while(!Exists());

        CreateScope();
        do
        {
            Thread.Sleep(1000);
        }while(!ScopeExists());

        CreateCollections();
        do
        {
            Thread.Sleep(1000);
        }while(!CollectionsExists());
    }

    private void CreateScope()
    {
        var manager = _bucket.Collections;
        var scopes = manager.GetAllScopesAsync().GetAwaiter().GetResult();
        if(!scopes.Contains(new ScopeSpec(_couchbaseDbContextOptionsBuilder.Bucket)))
        {
            try
            {
                manager.CreateScopeAsync(_couchbaseDbContextOptionsBuilder.Scope).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private void CreateCollections()
    {
        var manager = _bucket.Collections;
        foreach (var entityType in _designTimeModel.Model.GetEntityTypes())
        {
            try
            {
                manager.CreateCollectionAsync(
                        new CollectionSpec(
                            _couchbaseDbContextOptionsBuilder.Bucket, entityType.Name))
                    .GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public override void Delete()
    {
        InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        var manager = _bucket.Cluster.Buckets;
        manager.DropBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).GetAwaiter().GetResult();
    }
}