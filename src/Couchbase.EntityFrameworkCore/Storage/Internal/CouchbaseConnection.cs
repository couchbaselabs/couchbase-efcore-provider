using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection : IRelationalConnection
{
    private IDbContextTransaction? _currentTransaction;
    private IDbContextTransaction? _currentTransaction1;

    public void ResetState()
    {
        throw new NotImplementedException();
    }

    public Task ResetStateAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public IDbContextTransaction BeginTransaction()
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void CommitTransaction()
    {
        throw new NotImplementedException();
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void RollbackTransaction()
    {
        throw new NotImplementedException();
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    IDbContextTransaction? IRelationalConnection.CurrentTransaction => _currentTransaction1;

    public void SetDbConnection(DbConnection? value, bool contextOwnsConnection)
    {
        throw new NotImplementedException();
    }

    public bool Open(bool errorsExpected = false)
    {
        throw new NotImplementedException();
    }

    public Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        throw new NotImplementedException();
    }

    public bool Close()
    {
        throw new NotImplementedException();
    }

    public Task<bool> CloseAsync()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommand RentCommand()
    {
        throw new NotImplementedException();
    }

    public void ReturnCommand(IRelationalCommand command)
    {
        throw new NotImplementedException();
    }

    public string? ConnectionString { get; set; }
    public DbConnection DbConnection { get; set; }
    public DbContext Context { get; }
    public Guid ConnectionId { get; }
    public int? CommandTimeout { get; set; }

    IDbContextTransaction? IDbContextTransactionManager.CurrentTransaction => _currentTransaction;

    public IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public IDbContextTransaction? UseTransaction(DbTransaction? transaction)
    {
        throw new NotImplementedException();
    }

    public IDbContextTransaction? UseTransaction(DbTransaction? transaction, Guid transactionId)
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction?> UseTransactionAsync(DbTransaction? transaction, CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction?> UseTransactionAsync(DbTransaction? transaction, Guid transactionId,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }
}