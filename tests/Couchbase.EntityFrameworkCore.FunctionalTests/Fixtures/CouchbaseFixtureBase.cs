using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Microsoft.EntityFrameworkCore;
using Xunit;
using BucketNotFoundException = Couchbase.Core.Exceptions.BucketNotFoundException;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public abstract class CouchbaseFixtureBase : IDisposable, IAsyncDisposable, IAsyncLifetime
{
    private bool _disposed;
    private bool _created;
    private ClusterOptions _options;
    private bool _initialized;
    private ICluster _cluster;
    
    public DbContext DbContext {get; protected set;}
    
    protected ClusterOptions Options { get; set; }

    public ICluster Cluster => _cluster;

    protected virtual string DatabaseName { get; } = "default";
    
    protected virtual string ScopeName { get; } = "_default";

    protected abstract Task LoadDataAsync();

    protected virtual async Task CreateCollectionAsync()
    {
        var bucket = await Cluster.BucketAsync(DatabaseName);
        await bucket.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));

        var manager = bucket.Collections;

        var scopes = await manager.GetAllScopesAsync();
        if (scopes.FirstOrDefault(x => x.Name == ScopeName) == null)
        {
            await manager.CreateScopeAsync(ScopeName);
        }

        await Task.Delay(1000);

        scopes = await manager.GetAllScopesAsync();
        var scope = scopes.First(x => x.Name == ScopeName);
        foreach (var entityType in DbContext.Model.GetEntityTypes())
        {
            var collectionName = entityType.ClrType.Name.ToLower();
            if (scope.Collections.FirstOrDefault(x =>
                    x.Name == collectionName) == null)
            {
                await manager.CreateCollectionAsync(ScopeName,
                    collectionName,
                    new CreateCollectionSettings());
            }
        }
        
        await Task.Delay(1000);
    }

    protected virtual async Task CreateBucketAsync()
    {
        try
        {
            var manager = Cluster.Buckets;
            await manager.CreateBucketAsync(new BucketSettings
            {
                Name = DatabaseName,
                BucketType = BucketType.Couchbase,
                StorageBackend = StorageBackend.Magma,
                NumVBuckets = 128,
                RamQuotaMB = 312,
                FlushEnabled = true
            });

            await Task.Delay(5000);
            _created = true;
        }
        catch (BucketExistsException)
        {
            //ignore
        }
    }

    private async Task FlushBucketAsync()
    {
        try
        {
            var manager = Cluster.Buckets;
            await manager.FlushBucketAsync(DatabaseName);
        }
        catch (BucketIsNotFlushableException)
        {
            //ignore
        }
    }

    protected virtual async Task<bool> BucketExistsAsync()
    {
        var manager = Cluster.Buckets;
        try
        {
            await manager.GetBucketAsync(DatabaseName);
            return true;
        }
        catch (BucketNotFoundException)
        {
            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    protected virtual async Task DropBucketAsync()
    {
        try
        {
            var manager = Cluster.Buckets;
            await manager.DropBucketAsync(DatabaseName);
        }
        catch(BucketNotFoundException){}
        catch (Exception e)
        {
            throw;
        }
    }

    public virtual async Task InitializeAsync()
    {
        if (!_initialized)
        {
            _cluster = await Couchbase.Cluster.ConnectAsync("couchbase://localhost", 
                new ClusterOptions().
                    WithPasswordAuthentication("Administrator", "password"));
            
            await Cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));

            if (await BucketExistsAsync())
            {
                await FlushBucketAsync();
                await CreateCollectionAsync();
                await LoadDataAsync();
            }
            else
            {
                await CreateBucketAsync();
                await CreateCollectionAsync();
                await LoadDataAsync();
            }
            _initialized = true;
        }
    }

    public virtual void Dispose()
    {
        DbContext?.Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Cluster.DisposeAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
    }
}