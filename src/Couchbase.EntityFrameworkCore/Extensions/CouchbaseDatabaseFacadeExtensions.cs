using System.Data;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseDatabaseFacadeExtensions
{
    /// <summary>
    /// Begins a Couchbase transaction with the specified durability level.
    /// </summary>
    /// <param name="databaseFacade">The database facade.</param>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <returns>A transaction that can be committed or rolled back.</returns>
    public static IDbContextTransaction BeginCouchbaseTransaction(
        this DatabaseFacade databaseFacade,
        DurabilityLevel durabilityLevel)
    {
        return BeginCouchbaseTransaction(databaseFacade, durabilityLevel, IsolationLevel.Unspecified);
    }

    /// <summary>
    /// Begins a Couchbase transaction with the specified durability and isolation levels.
    /// </summary>
    /// <param name="databaseFacade">The database facade.</param>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <param name="isolationLevel">The isolation level (informational only for Couchbase).</param>
    /// <returns>A transaction that can be committed or rolled back.</returns>
    public static IDbContextTransaction BeginCouchbaseTransaction(
        this DatabaseFacade databaseFacade,
        DurabilityLevel durabilityLevel,
        IsolationLevel isolationLevel)
    {
        var relationalConnection = databaseFacade.GetService<IRelationalConnection>();
        if (relationalConnection is not CouchbaseRelationalConnection couchbaseConnection)
        {
            throw new InvalidOperationException(
                "BeginCouchbaseTransaction can only be used with a Couchbase database provider.");
        }

        return couchbaseConnection.BeginCouchbaseTransaction(durabilityLevel, isolationLevel);
    }

    /// <summary>
    /// Begins a Couchbase transaction asynchronously with the specified durability level.
    /// </summary>
    /// <param name="databaseFacade">The database facade.</param>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A transaction that can be committed or rolled back.</returns>
    public static Task<IDbContextTransaction> BeginCouchbaseTransactionAsync(
        this DatabaseFacade databaseFacade,
        DurabilityLevel durabilityLevel,
        CancellationToken cancellationToken = default)
    {
        return BeginCouchbaseTransactionAsync(databaseFacade, durabilityLevel, IsolationLevel.Unspecified, cancellationToken);
    }

    /// <summary>
    /// Begins a Couchbase transaction asynchronously with the specified durability and isolation levels.
    /// </summary>
    /// <param name="databaseFacade">The database facade.</param>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <param name="isolationLevel">The isolation level (informational only for Couchbase).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A transaction that can be committed or rolled back.</returns>
    public static async Task<IDbContextTransaction> BeginCouchbaseTransactionAsync(
        this DatabaseFacade databaseFacade,
        DurabilityLevel durabilityLevel,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        var relationalConnection = databaseFacade.GetService<IRelationalConnection>();
        if (relationalConnection is not CouchbaseRelationalConnection couchbaseConnection)
        {
            throw new InvalidOperationException(
                "BeginCouchbaseTransactionAsync can only be used with a Couchbase database provider.");
        }

        return await couchbaseConnection.BeginCouchbaseTransactionAsync(durabilityLevel, isolationLevel, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ICluster> GetCouchbaseClientAsync(
        this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString()
            ?? throw new InvalidOperationException("No Couchbase connection string is configured.");
        var options = new ClusterOptions().WithConnectionString(connectionString);
        if (options.TryGetRawParameter("bucket", out var bucketName) && bucketName != null)
        {
            var bucketProvider = databaseFacade.GetService<IBucketProvider>();
            var bucket = await bucketProvider.GetBucketAsync(bucketName.ToString()!).ConfigureAwait(false);
            return bucket.Cluster;
        }
        throw new CouchbaseException("No couchbase connection string found.");
    }

    public static ICluster GetCouchbaseClient(this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString()
            ?? throw new InvalidOperationException("No Couchbase connection string is configured.");
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
        var connectionString = databaseFacade.GetConnectionString()
            ?? throw new InvalidOperationException("No Couchbase connection string is configured.");
        var clusterOptions = new ClusterOptions().WithConnectionString(connectionString);

        if (clusterOptions.TryGetRawParameter("bucket", out object? bucketName) && bucketName != null)
        {
            var couchbaseClient = GetCouchbaseClient(databaseFacade);
            couchbaseClient.Buckets.FlushBucketAsync(bucketName.ToString()!).GetAwaiter().GetResult();
        }
    }

    public static async Task EnsureCleanAsync(this DatabaseFacade databaseFacade)
    {
        var connectionString = databaseFacade.GetConnectionString()
            ?? throw new InvalidOperationException("No Couchbase connection string is configured.");
        var clusterOptions = new ClusterOptions().WithConnectionString(connectionString);

        if (clusterOptions.TryGetRawParameter("bucket", out var bucketName) && bucketName != null)
        {
            var couchbaseClient = await GetCouchbaseClientAsync(databaseFacade).ConfigureAwait(false);
            await couchbaseClient.Buckets.FlushBucketAsync(bucketName.ToString()!).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the number of operations committed in a Couchbase transaction.
    /// </summary>
    /// <param name="transaction">The transaction to get the count from.</param>
    /// <returns>The number of committed operations, or 0 if not a Couchbase transaction.</returns>
    public static int GetCommittedCount(this IDbContextTransaction transaction)
    {
        if (transaction is ICouchbaseDbContextTransaction couchbaseTransaction)
        {
            return couchbaseTransaction.CommittedCount;
        }
        
        // Try to get from the underlying DbTransaction
        var dbTransaction = transaction.GetDbTransaction();
        if (dbTransaction is CouchbaseDbTransaction couchbaseDbTransaction)
        {
            return couchbaseDbTransaction.CommittedCount;
        }

        return 0;
    }
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
