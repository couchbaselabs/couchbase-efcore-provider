using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Infrastructure;

public class CouchbaseDbContextOptionsBuilder : ICouchbaseDbContextOptionsBuilder
{
    private readonly string _connectionString;

    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, string connectionString)
    {
        _connectionString = connectionString;
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = new ClusterOptions().WithConnectionString(connectionString);
    }

    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, ClusterOptions clusterOptions)
    {
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = clusterOptions;
    }

    //TODO temp
    public string ConnectionString => ClusterOptions.ConnectionString! + $"?bucket={Bucket}";

    public DbContextOptionsBuilder OptionsBuilder { get; }

    public ClusterOptions ClusterOptions { get; }

    public string Bucket { get; set; }

    public string Scope { get; set; }

    /// <summary>
    /// Gets or sets the default durability level for transactions.
    /// Defaults to <see cref="DurabilityLevel.Majority"/> for production safety.
    /// Set to <see cref="DurabilityLevel.None"/> for single-node development/test clusters.
    /// </summary>
    public DurabilityLevel TransactionDurabilityLevel { get; set; } = DurabilityLevel.Majority;

    DbContextOptionsBuilder ICouchbaseDbContextOptionsBuilder.OptionsBuilder => OptionsBuilder;
}

public interface ICouchbaseDbContextOptionsBuilder
{
    DbContextOptionsBuilder OptionsBuilder { get; }

    ClusterOptions ClusterOptions { get; }

    public string ConnectionString { get; }

    public string Bucket { get; set; }

    public string Scope { get; set; }

    /// <summary>
    /// Gets or sets the default durability level for transactions.
    /// Defaults to <see cref="DurabilityLevel.Majority"/> for production safety.
    /// Set to <see cref="DurabilityLevel.None"/> for single-node development/test clusters.
    /// </summary>
    public DurabilityLevel TransactionDurabilityLevel { get; set; }
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
