using System.Data;
using System.Data.Common;
using Couchbase.Client.Transactions.Config;
using Couchbase.KeyValue;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// A provider-specific DbTransaction that buffers mutations and commits them
/// via Couchbase distributed transactions when Commit is called.
/// </summary>
public class CouchbaseDbTransaction : DbTransaction
{
    private readonly CouchbaseConnection _connection;
    private readonly ICluster _cluster;
    private readonly ILogger? _logger;
    private readonly DurabilityLevel _durabilityLevel;
    private readonly List<TransactionOperation> _pendingOperations = new();
    private bool _disposed;
    private bool _completed;

    /// <summary>
    /// Creates a new Couchbase transaction with the specified durability level.
    /// </summary>
    /// <param name="connection">The parent connection.</param>
    /// <param name="cluster">The Couchbase cluster.</param>
    /// <param name="isolationLevel">The isolation level (informational only for Couchbase).</param>
    /// <param name="durabilityLevel">
    /// The durability level for the transaction. Defaults to <see cref="DurabilityLevel.Majority"/>.
    /// Use <see cref="DurabilityLevel.None"/> for single-node development/test clusters.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public CouchbaseDbTransaction(
        CouchbaseConnection connection, 
        ICluster cluster, 
        IsolationLevel isolationLevel, 
        DurabilityLevel durabilityLevel = DurabilityLevel.Majority,
        ILogger? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _durabilityLevel = durabilityLevel;
        _logger = logger;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }

    protected override DbConnection DbConnection => _connection;

    internal IReadOnlyList<TransactionOperation> PendingOperations => _pendingOperations;

    internal bool IsCompleted => _completed;

    /// <summary>
    /// Enqueue an insert operation to be executed when the transaction commits.
    /// </summary>
    public void EnqueueInsert(ICouchbaseCollection collection, string id, object content)
    {
        ThrowIfCompleted();
        _pendingOperations.Add(new TransactionOperation(TransactionOperationType.Insert, collection, id, content));
    }

    /// <summary>
    /// Enqueue an upsert (replace) operation to be executed when the transaction commits.
    /// </summary>
    public void EnqueueUpsert(ICouchbaseCollection collection, string id, object content)
    {
        ThrowIfCompleted();
        _pendingOperations.Add(new TransactionOperation(TransactionOperationType.Upsert, collection, id, content));
    }

    /// <summary>
    /// Enqueue a remove operation to be executed when the transaction commits.
    /// </summary>
    public void EnqueueRemove(ICouchbaseCollection collection, string id)
    {
        ThrowIfCompleted();
        _pendingOperations.Add(new TransactionOperation(TransactionOperationType.Remove, collection, id, null));
    }

    public override void Commit()
    {
        CommitAsync().GetAwaiter().GetResult();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();

        if (_pendingOperations.Count == 0)
        {
            _completed = true;
            return;
        }

        try
        {
            var perTxnConfig = PerTransactionConfigBuilder.Create()
                .DurabilityLevel(_durabilityLevel)
                .Build();

            await _cluster.Transactions.RunAsync(async ctx =>
            {
                foreach (var op in _pendingOperations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    switch (op.OperationType)
                    {
                        case TransactionOperationType.Insert:
                            await ctx.InsertAsync(op.Collection, op.Id, op.Content!).ConfigureAwait(false);
                            break;

                        case TransactionOperationType.Upsert:
                            var existingDoc = await ctx.GetOptionalAsync(op.Collection, op.Id).ConfigureAwait(false);
                            if (existingDoc != null)
                            {
                                await ctx.ReplaceAsync(existingDoc, op.Content!).ConfigureAwait(false);
                            }
                            else
                            {
                                await ctx.InsertAsync(op.Collection, op.Id, op.Content!).ConfigureAwait(false);
                            }
                            break;

                        case TransactionOperationType.Remove:
                            var docToRemove = await ctx.GetOptionalAsync(op.Collection, op.Id).ConfigureAwait(false);
                            if (docToRemove != null)
                            {
                                await ctx.RemoveAsync(docToRemove).ConfigureAwait(false);
                            }
                            else
                            {
                                _logger?.LogWarning(
                                    "Document with id '{Id}' not found in collection during transaction remove. " +
                                    "The document may have been deleted outside the transaction or the key format may be incorrect.",
                                    op.Id);
                            }
                            break;
                    }
                }
            }, perTxnConfig).ConfigureAwait(false);

            _completed = true;
            _pendingOperations.Clear();
        }
        catch (Couchbase.Client.Transactions.Error.TransactionFailedException ex)
        {
            _logger?.LogError(ex, 
                "Transaction commit failed. TransactionId: {TransactionId}, Cause: {Cause}",
                ex.Result?.TransactionId,
                ex.InnerException?.Message ?? ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Transaction commit failed with unexpected error");
            throw;
        }
    }

    public override void Rollback()
    {
        ThrowIfCompleted();
        _pendingOperations.Clear();
        _completed = true;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();
        return Task.CompletedTask;
    }

    private void ThrowIfCompleted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("This transaction has already been completed.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing && !_completed)
            {
                _pendingOperations.Clear();
            }
            _disposed = true;
            _completed = true;
            _connection.ClearCurrentTransaction();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (!_completed)
            {
                _pendingOperations.Clear();
            }
            _disposed = true;
            _completed = true;
            _connection.ClearCurrentTransaction();
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

internal enum TransactionOperationType
{
    Insert,
    Upsert,
    Remove
}

internal readonly struct TransactionOperation
{
    public TransactionOperationType OperationType { get; }
    public ICouchbaseCollection Collection { get; }
    public string Id { get; }
    public object? Content { get; }

    public TransactionOperation(TransactionOperationType operationType, ICouchbaseCollection collection, string id, object? content)
    {
        OperationType = operationType;
        Collection = collection;
        Id = id;
        Content = content;
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
