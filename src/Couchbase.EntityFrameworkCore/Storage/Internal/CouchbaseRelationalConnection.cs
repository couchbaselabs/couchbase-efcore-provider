using System.Data;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// A Couchbase implementation of IRelationalConnection that provides connection
/// management and transaction support for Entity Framework Core.
/// </summary>
public class CouchbaseRelationalConnection : RelationalConnection, ICouchbaseConnection
{
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Infrastructure> _logger;
    private readonly ILoggerFactory? _loggerFactory;

    public CouchbaseRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger,
        IRelationalCommandBuilder relationalCommandBuilder,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
        : base(dependencies)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bucketProvider = bucketProvider ?? throw new ArgumentNullException(nameof(bucketProvider));
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder ?? throw new ArgumentNullException(nameof(couchbaseDbContextOptionsBuilder));

        var optionsExtension = dependencies.ContextOptions.Extensions.OfType<CouchbaseOptionsExtension>().FirstOrDefault();
        _loggerFactory = optionsExtension?.DbContextOptionsBuilder?.ClusterOptions?.Logging;
    }

    /// <summary>
    /// Gets the bucket provider for accessing Couchbase buckets.
    /// </summary>
    public IBucketProvider BucketProvider => _bucketProvider;

    /// <summary>
    /// Gets the Couchbase context options.
    /// </summary>
    public ICouchbaseDbContextOptionsBuilder CouchbaseOptions => _couchbaseDbContextOptionsBuilder;

    /// <summary>
    /// Gets the current Couchbase-specific transaction if one is active.
    /// </summary>
    public CouchbaseDbTransaction? CouchbaseTransaction
    {
        get
        {
            // Get the transaction from EF Core's CurrentTransaction property
            // which properly tracks the active transaction
            var efTransaction = CurrentTransaction;
            if (efTransaction == null)
            {
                return null;
            }

            // Extract the underlying DbTransaction from EF Core's wrapper
            try
            {
                var dbTransaction = efTransaction.GetDbTransaction();
                return dbTransaction as CouchbaseDbTransaction;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Creates the underlying Couchbase DbConnection.
    /// </summary>
    protected override DbConnection CreateDbConnection()
    {
        var connLogger = _loggerFactory?.CreateLogger<CouchbaseConnection>();
        return new CouchbaseConnection(_bucketProvider, _couchbaseDbContextOptionsBuilder, connLogger);
    }

    /// <summary>
    /// Couchbase does not support ambient transactions.
    /// </summary>
    protected override bool SupportsAmbientTransactions => false;

    /// <summary>
    /// Opens the connection to Couchbase.
    /// </summary>
    public override bool Open(bool errorsExpected = false)
    {
        var wasOpened = base.Open(errorsExpected);
        if (wasOpened)
        {
            _logger.Logger.LogDebug("Couchbase relational connection opened to bucket {Bucket}",
                _couchbaseDbContextOptionsBuilder.Bucket);
        }
        return wasOpened;
    }

    /// <summary>
    /// Opens the connection to Couchbase asynchronously.
    /// </summary>
    public override async Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        var wasOpened = await base.OpenAsync(cancellationToken, errorsExpected).ConfigureAwait(false);
        if (wasOpened)
        {
            _logger.Logger.LogDebug("Couchbase relational connection opened asynchronously to bucket {Bucket}",
                _couchbaseDbContextOptionsBuilder.Bucket);
        }
        return wasOpened;
    }

    /// <summary>
    /// Closes the connection to Couchbase.
    /// </summary>
    public override bool Close()
    {
        var wasClosed = base.Close();
        if (wasClosed)
        {
            _logger.Logger.LogDebug("Couchbase relational connection closed");
        }
        return wasClosed;
    }

    /// <summary>
    /// Closes the connection to Couchbase asynchronously.
    /// </summary>
    public override async Task<bool> CloseAsync()
    {
        var wasClosed = await base.CloseAsync().ConfigureAwait(false);
        if (wasClosed)
        {
            _logger.Logger.LogDebug("Couchbase relational connection closed asynchronously");
        }
        return wasClosed;
    }

    /// <summary>
    /// Specifies an existing DbTransaction to be used. Only CouchbaseDbTransaction is supported.
    /// </summary>
    public override IDbContextTransaction? UseTransaction(DbTransaction? transaction, Guid transactionId)
    {
        if (transaction == null)
        {
            return base.UseTransaction(null, transactionId);
        }

        if (transaction is not CouchbaseDbTransaction)
        {
            throw new InvalidOperationException(
                "Couchbase provider only supports using CouchbaseDbTransaction instances. " +
                "External DbTransaction instances from other providers cannot be shared.");
        }

        return base.UseTransaction(transaction, transactionId);
    }

    /// <summary>
    /// Specifies an existing DbTransaction to be used asynchronously. Only CouchbaseDbTransaction is supported.
    /// </summary>
    public override async Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        if (transaction == null)
        {
            return await base.UseTransactionAsync(null, transactionId, cancellationToken).ConfigureAwait(false);
        }

        if (transaction is not CouchbaseDbTransaction)
        {
            throw new InvalidOperationException(
                "Couchbase provider only supports using CouchbaseDbTransaction instances. " +
                "External DbTransaction instances from other providers cannot be shared.");
        }

        return await base.UseTransactionAsync(transaction, transactionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a Couchbase transaction with the specified durability level.
    /// </summary>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <param name="isolationLevel">The isolation level (informational only for Couchbase).</param>
    /// <returns>A transaction that can be committed or rolled back.</returns>
    public IDbContextTransaction BeginCouchbaseTransaction(
        DurabilityLevel durabilityLevel,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified)
    {
        // Temporarily override the default durability level
        var originalDurability = _couchbaseDbContextOptionsBuilder.TransactionDurabilityLevel;
        try
        {
            _couchbaseDbContextOptionsBuilder.TransactionDurabilityLevel = durabilityLevel;
            return BeginTransaction(isolationLevel);
        }
        finally
        {
            _couchbaseDbContextOptionsBuilder.TransactionDurabilityLevel = originalDurability;
        }
    }

    /// <summary>
    /// Begins a Couchbase transaction asynchronously with the specified durability level.
    /// </summary>
    /// <param name="durabilityLevel">The durability level for this transaction.</param>
    /// <param name="isolationLevel">The isolation level (informational only for Couchbase).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A transaction that can be committed or rolled back.</returns>
    public async Task<IDbContextTransaction> BeginCouchbaseTransactionAsync(
        DurabilityLevel durabilityLevel,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        // Temporarily override the default durability level
        var originalDurability = _couchbaseDbContextOptionsBuilder.TransactionDurabilityLevel;
        try
        {
            _couchbaseDbContextOptionsBuilder.TransactionDurabilityLevel = durabilityLevel;
            return await BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _couchbaseDbContextOptionsBuilder.TransactionDurabilityLevel = originalDurability;
        }
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
