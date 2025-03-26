using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection2 : IRelationalConnection
{
    private IDbContextTransaction? _currentTransaction;
    private IDbContextTransaction? _currentTransaction1;
    private IRelationalCommand _relationalCommand;
    //private IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IRelationalCommandBuilder _relationalCommandBuilder;
    private readonly RelationalConnectionDependencies _dependencies;
    private IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger;
    private IRelationalCommand  _cachedRelationalCommand;
    

    public CouchbaseConnection2(RelationalConnectionDependencies dependencies, IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger, IRelationalCommandBuilder relationalCommandBuilder)
    {
        _dependencies = dependencies;
        this.logger = logger;
        //_rawSqlCommandBuilder = rawSqlCommandBuilder;
        _relationalCommandBuilder = relationalCommandBuilder;
    }
    
    public void ResetState()
    {
        throw new NotImplementedException();
    }

    public Task ResetStateAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IDbContextTransaction BeginTransaction()
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void CommitTransaction()
    {
        throw new NotImplementedException();
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void RollbackTransaction()
    {
        throw new NotImplementedException();
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public string? ConnectionString { get; set; }
    public DbConnection DbConnection { get; set; }
    public void SetDbConnection(DbConnection? value, bool contextOwnsConnection)
    {
        throw new NotImplementedException();
    }

    public DbContext Context { get; }
    public Guid ConnectionId { get; }
    public int? CommandTimeout { get; set; }
    public bool Open(bool errorsExpected = false)
    {
        throw new NotImplementedException();
    }

    public Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        throw new NotImplementedException();
    }

    public IRelationalCommand RentCommand()
    {
        var command = _cachedRelationalCommand;

        if (command is null)
        {
           return _relationalCommandBuilder.Build();
          // return _rawSqlCommandBuilder.Build()
        }

        _cachedRelationalCommand = null;
        return command;
    }

    public void ReturnCommand(IRelationalCommand command)
    {
        throw new NotImplementedException();
    }

    public IDbContextTransaction? CurrentTransaction { get; }
    public IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
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

    public Task<IDbContextTransaction?> UseTransactionAsync(DbTransaction? transaction, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IDbContextTransaction?> UseTransactionAsync(DbTransaction? transaction, Guid transactionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    #region Close & Dispose
    public bool Close()
    {
        //Nothing to dispose...atm
        //throw new NotImplementedException();
        return true;
    }

    public Task<bool> CloseAsync()
    {
        //Nothing to dispose...atm
       // throw new NotImplementedException();
       return Task.FromResult(true);
    }

    public void Dispose()
    {
        //Nothing to dispose...atm
        //throw new NotImplementedException();
    }

    public ValueTask DisposeAsync()
    {
        //Nothing to dispose...atm
        //throw new NotImplementedException();
        return ValueTask.CompletedTask;
    }
    #endregion
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2021 Couchbase, Inc.
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
