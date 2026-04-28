using System.Data;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// A DbConnection implementation for Couchbase that manages connection state
/// and provides transaction support via CouchbaseDbTransaction.
/// </summary>
public class CouchbaseConnection : DbConnection
{
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly ILogger? _logger;
    private ConnectionState _state = ConnectionState.Closed;
    private ICluster? _cluster;
    private CouchbaseDbTransaction? _currentTransaction;
    private string? _database;
    private string? _dataSource;
    private string? _serverVersion;

    public CouchbaseConnection(
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder,
        ILogger<CouchbaseConnection>? logger = null)
    {
        _bucketProvider = bucketProvider ?? throw new ArgumentNullException(nameof(bucketProvider));
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder ?? throw new ArgumentNullException(nameof(couchbaseDbContextOptionsBuilder));
        _logger = logger;
        _database = couchbaseDbContextOptionsBuilder.Bucket;
    }

    public CouchbaseConnection(
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder,
        string database,
        string dataSource,
        string serverVersion,
        ILogger<CouchbaseConnection>? logger = null)
    {
        _bucketProvider = bucketProvider ?? throw new ArgumentNullException(nameof(bucketProvider));
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder ?? throw new ArgumentNullException(nameof(couchbaseDbContextOptionsBuilder));
        _database = database;
        _dataSource = dataSource;
        _serverVersion = serverVersion;
        _logger = logger;
    }

    public override string ConnectionString
    {
        get => _couchbaseDbContextOptionsBuilder.ConnectionString;
        set => throw new NotSupportedException("Couchbase connection string cannot be changed after creation.");
    }

    public override string Database => _database ?? _couchbaseDbContextOptionsBuilder.Bucket;

    public override ConnectionState State => _state;

    public override string DataSource => _dataSource ?? ExtractDataSource();

    public override string ServerVersion => _serverVersion ?? "Couchbase";

    /// <summary>
    /// Gets the current Couchbase cluster instance when connected.
    /// </summary>
    public ICluster? Cluster => _cluster;

    /// <summary>
    /// Gets the current active transaction, if any.
    /// </summary>
    public CouchbaseDbTransaction? CurrentTransaction => _currentTransaction;

    /// <summary>
    /// Gets the bucket provider for accessing Couchbase buckets.
    /// </summary>
    public IBucketProvider BucketProvider => _bucketProvider;

    /// <summary>
    /// Gets the Couchbase context options.
    /// </summary>
    public ICouchbaseDbContextOptionsBuilder Options => _couchbaseDbContextOptionsBuilder;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        // Default to DurabilityLevel.None for compatibility with single-node development clusters.
        // Production deployments should use BeginCouchbaseTransaction/BeginCouchbaseTransactionAsync
        // with an explicit durability level (e.g., DurabilityLevel.Majority) for data safety.
        return BeginDbTransaction(isolationLevel, DurabilityLevel.None);
    }

    /// <summary>
    /// Begins a transaction with the specified isolation level and durability level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level (informational only for Couchbase).</param>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <returns>A new <see cref="CouchbaseDbTransaction"/>.</returns>
    internal CouchbaseDbTransaction BeginDbTransaction(IsolationLevel isolationLevel, DurabilityLevel durabilityLevel)
    {
        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection must be open to begin a transaction.");
        }

        if (_currentTransaction != null && !_currentTransaction.IsCompleted)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        if (_cluster == null)
        {
            throw new InvalidOperationException("Cluster is not available. Ensure the connection is properly opened.");
        }

        _currentTransaction = new CouchbaseDbTransaction(
            this, 
            _cluster, 
            isolationLevel, 
            durabilityLevel,
            _logger);
        return _currentTransaction;
    }

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("Couchbase does not support changing the database after connection is established.");
    }

    public override void Close()
    {
        if (_state == ConnectionState.Closed)
        {
            return;
        }

        _state = ConnectionState.Closed;
        _currentTransaction?.Dispose();
        _currentTransaction = null;
        _logger?.LogDebug("Couchbase connection closed");
    }

    public override async Task CloseAsync()
    {
        Close();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override void Open()
    {
        OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_state == ConnectionState.Open)
        {
            return;
        }

        _state = ConnectionState.Connecting;
        try
        {
            var bucket = await _bucketProvider.GetBucketAsync(_couchbaseDbContextOptionsBuilder.Bucket).ConfigureAwait(false);
            _cluster = bucket.Cluster;
            _state = ConnectionState.Open;
            _logger?.LogDebug("Couchbase connection opened to bucket {Bucket}", _couchbaseDbContextOptionsBuilder.Bucket);
        }
        catch (Exception ex)
        {
            _state = ConnectionState.Broken;
            _logger?.LogError(ex, "Failed to open Couchbase connection");
            throw;
        }
    }

    protected override DbCommand CreateDbCommand()
    {
        if (_state != ConnectionState.Open)
        {
            Open();
        }

        return new CouchbaseCommand
        {
            Connection = this,
            Cluster = _cluster!
        };
    }

    internal void ClearCurrentTransaction()
    {
        _currentTransaction = null;
    }

    private string ExtractDataSource()
    {
        var connStr = _couchbaseDbContextOptionsBuilder.ClusterOptions?.ConnectionString;
        if (string.IsNullOrEmpty(connStr))
        {
            return "couchbase://localhost";
        }

        var idx = connStr.IndexOf('?');
        return idx > 0 ? connStr[..idx] : connStr;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
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
