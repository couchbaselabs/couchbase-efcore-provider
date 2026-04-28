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
/// Wraps an IDbContextTransaction to handle AcceptAllChanges on commit success
/// when using Couchbase transactions with deferred change tracking.
/// </summary>
internal sealed class CouchbaseContextTransaction : ICouchbaseDbContextTransaction
{
    private readonly IDbContextTransaction _inner;
    private readonly DbContext _context;
    private readonly CouchbaseSaveChangesInterceptor _interceptor;
    private int _committedCount;

    public CouchbaseContextTransaction(
        IDbContextTransaction inner,
        DbContext context,
        CouchbaseSaveChangesInterceptor interceptor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        
        _interceptor.BeginTracking();
    }

    public Guid TransactionId => _inner.TransactionId;

    /// <inheritdoc />
    public int CommittedCount => _committedCount;

    public void Commit()
    {
        _inner.Commit();
        _committedCount = GetUnderlyingTransactionCommittedCount();
        _interceptor.AcceptTrackedChanges(_context);
        _interceptor.EndTracking();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _inner.CommitAsync(cancellationToken).ConfigureAwait(false);
        _committedCount = GetUnderlyingTransactionCommittedCount();
        _interceptor.AcceptTrackedChanges(_context);
        _interceptor.EndTracking();
    }

    public void Rollback()
    {
        _inner.Rollback();
        _interceptor.EndTracking();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _inner.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _interceptor.EndTracking();
    }

    public DbTransaction GetDbTransaction() => _inner.GetDbTransaction();

    public void Dispose()
    {
        _interceptor.EndTracking();
        _inner.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _interceptor.EndTracking();
        await _inner.DisposeAsync().ConfigureAwait(false);
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
