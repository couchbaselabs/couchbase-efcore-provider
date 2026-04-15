using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public static class FixtureExtensions
{
    public static async Task CreateCollectionsFromModelAsync(this DbContext dbContext, string databaseName, string scopeName)
    {
        var cluster = await dbContext.Database.GetCouchbaseClientAsync();
        var bucket = await cluster.BucketAsync(databaseName);
        var manager = bucket.Collections;

        var scopes = await manager.GetAllScopesAsync();
        if (scopes.FirstOrDefault(x => x.Name == scopeName) == null)
        {
            await manager.CreateScopeAsync(scopeName);
        }

        await Task.Delay(1000);

        scopes = await manager.GetAllScopesAsync();
        var scope = scopes.First(x => x.Name == scopeName);
        foreach (var entityType in dbContext.Model.GetEntityTypes())
        {
            var collectionName = entityType.ClrType.Name;
            if (scope.Collections.FirstOrDefault(x =>
                    x.Name == collectionName) == null)
            {
                await manager.CreateCollectionAsync(scopeName,
                    collectionName.ToLower(),
                    new CreateCollectionSettings());
            }
        }
        
        await Task.Delay(1000);
    }
    
    private static async Task CreateBucketAsync(this DbContext dbContext, string databaseName)
    {
        try
        {
            var cluster = await dbContext.Database.GetCouchbaseClientAsync();
            var manager = cluster.Buckets;
            await manager.CreateBucketAsync(new BucketSettings
            {
                Name = databaseName,
                BucketType = BucketType.Couchbase,
                StorageBackend = StorageBackend.Magma,
                NumVBuckets = 128,
                RamQuotaMB = 312,
                FlushEnabled = true
            });

            await Task.Delay(5000);
        }
        catch (Exception e)
        {
            throw;
        }
    }

    public static async Task FlushBucketAsync(this DbContext dbContext, string databaseName)
    {
        try
        {
            var cluster = await dbContext.Database.GetCouchbaseClientAsync();
            var manager = cluster.Buckets;
            await manager.FlushBucketAsync(databaseName);
        }
        catch (Exception e)
        {
            throw;
        }
    }

    public static async Task<bool> BucketExistsAsync(this DbContext dbContext, string databaseName)
    {
        var cluster = await dbContext.Database.GetCouchbaseClientAsync();
        var manager = cluster.Buckets;
        try
        {
            await manager.GetBucketAsync(databaseName);
            return true;
        }
        catch (BucketNotFoundException e)
        {
            return false;
        }
    }

    public static async Task DropBucketAsync(this DbContext dbContext, string databaseName)
    {
        try
        {
            var cluster = await dbContext.Database.GetCouchbaseClientAsync();
            var manager = cluster.Buckets;
            await manager.DropBucketAsync(databaseName);
        }
        catch (Exception e)
        {
            throw;
        }
    }
}