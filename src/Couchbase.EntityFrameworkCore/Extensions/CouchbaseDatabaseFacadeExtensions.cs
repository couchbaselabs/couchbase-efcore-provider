using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseDatabaseFacadeExtensions
{
    public static async Task<ICluster> GetCouchbaseClientAsync(
        this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString();
        var options = new ClusterOptions().WithConnectionString(connectionString);
        if (options.TryGetRawParameter("bucket", out var bucketName))
        {
            var bucketProvider = databaseFacade.GetService<IBucketProvider>();
            var bucket = await bucketProvider.GetBucketAsync(bucketName.ToString()).ConfigureAwait(false);
            return bucket.Cluster;
        }
        throw new CouchbaseException("No couchbase connection string found.");
    }

    public static ICluster GetCouchbaseClient(this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString();
        return GetService<IClusterProvider>(databaseFacade, connectionString).GetClusterAsync().GetAwaiter().GetResult();
    }

    private static TService GetService<TService>(IInfrastructure<IServiceProvider> databaseFacade, string connectionString)
        where TService : class
    {
        var service =  databaseFacade.GetInfrastructure().GetKeyedService<TService>(connectionString.Split('?').First());
        if (service == null)
        {
            throw new InvalidOperationException("Couchbase service could not be resolved.");
        }

        return service;
    }

    public static void EnsureClean(this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString();
        var clusterOptions = new ClusterOptions().WithConnectionString(connectionString);

        if (clusterOptions.TryGetRawParameter("bucket", out object? bucketName) && bucketName != null)
        {
            var couchbaseClient = GetCouchbaseClient(databaseFacade);
            couchbaseClient.Buckets.FlushBucketAsync(bucketName.ToString()).GetAwaiter().GetResult();
        }
    }

    public static async Task EnsureCleanAsync(this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString();
        var clusterOptions = new ClusterOptions().WithConnectionString(connectionString);

        if (clusterOptions.TryGetRawParameter("bucket", out var bucketName) && bucketName != null)
        {
            var couchbaseClient = await GetCouchbaseClientAsync(databaseFacade).ConfigureAwait(false);
            await couchbaseClient.Buckets.FlushBucketAsync(bucketName.ToString()).ConfigureAwait(false);
        }
    }
}