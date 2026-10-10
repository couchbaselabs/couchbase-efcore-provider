using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// A Couchbase-specific transaction that provides access to the committed operation count
/// and handles deferred change tracking.
/// </summary>
public interface ICouchbaseDbContextTransaction : IDbContextTransaction
{
    /// <summary>
    /// Gets the number of operations that were successfully committed.
    /// This value is only valid after Commit/CommitAsync completes successfully.
    /// </summary>
    int CommittedCount { get; }
}

/// <summary>
/// Wraps an IDbContextTransaction for Couchbase transactions: it records what each SaveChanges
/// saved (see <see cref="CouchbaseSaveChangesInterceptor"/>) and puts those entities back to their
/// pending state whenever the work is not persisted — commit failure, rollback, disposal without
/// commit, or rollback to a savepoint.
/// </summary>
internal sealed class CouchbaseContextTransaction : ICouchbaseDbContextTransaction
{
    private readonly IDbContextTransaction _inner;
    private readonly DbContext _context;
    private int _committedCount;
    private bool _completed;

    // Savepoint name -> number of recorded SaveChanges when it was created, in creation order.
    // Mirrors CouchbaseDbTransaction's own savepoint list, which does the same for queued operations.
    private readonly List<(string Name, int BatchCount)> _savepoints = new();

    public CouchbaseContextTransaction(
        IDbContextTransaction inner,
        DbContext context)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        
        CouchbaseSaveChangesInterceptor.BeginTracking(_context);
    }

    public Guid TransactionId => _inner.TransactionId;

    /// <inheritdoc />
    public int CommittedCount => _committedCount;

    public void Commit()
    {
        try
        {
            _inner.Commit();
            _committedCount = GetUnderlyingTransactionCommittedCount();
            CouchbaseSaveChangesInterceptor.AcceptTrackedChanges(_context);
            _completed = true;
        }
        catch
        {
            // The commit failed: nothing was persisted, so the saved entities are pending again
            // and the same context can retry.
            CouchbaseSaveChangesInterceptor.RestoreTrackedChanges(_context);
            throw;
        }
        finally
        {
            CouchbaseSaveChangesInterceptor.EndTracking(_context);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _inner.CommitAsync(cancellationToken);
            _committedCount = GetUnderlyingTransactionCommittedCount();
            CouchbaseSaveChangesInterceptor.AcceptTrackedChanges(_context);
            _completed = true;
        }
        catch
        {
            CouchbaseSaveChangesInterceptor.RestoreTrackedChanges(_context);
            throw;
        }
        finally
        {
            CouchbaseSaveChangesInterceptor.EndTracking(_context);
        }
    }

    public void Rollback()
    {
        _inner.Rollback();
        _completed = true;
        CouchbaseSaveChangesInterceptor.RestoreTrackedChanges(_context);
        CouchbaseSaveChangesInterceptor.EndTracking(_context);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _inner.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
        CouchbaseSaveChangesInterceptor.RestoreTrackedChanges(_context);
        CouchbaseSaveChangesInterceptor.EndTracking(_context);
    }

    public DbTransaction GetDbTransaction() => _inner.GetDbTransaction();

    // IDbContextTransaction's savepoint members are default interface methods that throw
    // NotSupportedException, so they must be forwarded explicitly or savepoints would work on
    // Database.BeginTransaction() but not on BeginCouchbaseTransaction().
    public bool SupportsSavepoints => _inner.SupportsSavepoints;

    public void CreateSavepoint(string name)
    {
        _inner.CreateSavepoint(name);
        MarkSavepoint(name);
    }

    public async Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        await _inner.CreateSavepointAsync(name, cancellationToken).ConfigureAwait(false);
        MarkSavepoint(name);
    }

    public void RollbackToSavepoint(string name)
    {
        _inner.RollbackToSavepoint(name);
        RestoreToSavepoint(name);
    }

    public async Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        await _inner.RollbackToSavepointAsync(name, cancellationToken).ConfigureAwait(false);
        RestoreToSavepoint(name);
    }

    public void ReleaseSavepoint(string name)
    {
        _inner.ReleaseSavepoint(name);
        ForgetSavepoint(name);
    }

    public async Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        await _inner.ReleaseSavepointAsync(name, cancellationToken).ConfigureAwait(false);
        ForgetSavepoint(name);
    }

    private void MarkSavepoint(string name)
    {
        // Creating a savepoint with an existing name moves it, as in SQL.
        var existing = _savepoints.FindLastIndex(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (existing >= 0)
        {
            _savepoints.RemoveAt(existing);
        }

        _savepoints.Add((name, CouchbaseSaveChangesInterceptor.BatchCount(_context)));
    }

    // The queued operations after the savepoint were already discarded by CouchbaseDbTransaction;
    // here the entities saved after it go back to pending, and later savepoints are dropped. The
    // savepoint itself remains so it can be rolled back to again.
    private void RestoreToSavepoint(string name)
    {
        var index = _savepoints.FindLastIndex(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        CouchbaseSaveChangesInterceptor.RestoreTrackedChanges(_context, _savepoints[index].BatchCount);
        _savepoints.RemoveRange(index + 1, _savepoints.Count - index - 1);
    }

    private void ForgetSavepoint(string name)
    {
        var index = _savepoints.FindLastIndex(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (index >= 0)
        {
            _savepoints.RemoveRange(index, _savepoints.Count - index);
        }
    }

    public void Dispose()
    {
        EndWithoutCommit();
        _inner.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        EndWithoutCommit();
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    // Disposing without committing abandons the work, so the saved entities are pending again.
    private void EndWithoutCommit()
    {
        if (!_completed)
        {
            CouchbaseSaveChangesInterceptor.RestoreTrackedChanges(_context);
        }

        CouchbaseSaveChangesInterceptor.EndTracking(_context);
    }

    private int GetUnderlyingTransactionCommittedCount()
    {
        var dbTransaction = _inner.GetDbTransaction();
        if (dbTransaction is CouchbaseDbTransaction couchbaseTransaction)
        {
            return couchbaseTransaction.CommittedCount;
        }
        return 0;
    }
}
