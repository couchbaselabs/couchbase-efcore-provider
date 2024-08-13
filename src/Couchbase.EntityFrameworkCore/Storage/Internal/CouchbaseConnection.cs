using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices.ComTypes;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection : RelationalConnection, IDbConnection, ICouchbaseConnection
{
    private IDbContextTransaction? _currentTransaction;
    private IDbContextTransaction? _currentTransaction1;
    private IRelationalCommand _relationalCommand;
    private IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly RelationalConnectionDependencies _dependencies;
    private IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger;
    private string _connectionString;

    public CouchbaseConnection(RelationalConnectionDependencies dependencies, IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger, IRawSqlCommandBuilder rawSqlCommandBuilder) : base(dependencies)
    {
        _dependencies = dependencies;
        this.logger = logger;
        this._rawSqlCommandBuilder = rawSqlCommandBuilder;

        /*var optionsExtension = dependencies.ContextOptions.Extensions.OfType<CouchbaseOptionsExtension>().FirstOrDefault();
        if (optionsExtension != null)
        {

            var relationalOptions = RelationalOptionsExtension.Extract(dependencies.ContextOptions);
            _commandTimeout = relationalOptions.CommandTimeout;

            if (relationalOptions.Connection != null)
            {
                InitializeDbConnection(relationalOptions.Connection);
            }
        }*/
    }

    protected override DbConnection CreateDbConnection()
    {
     //   var connection = new CouchbaseConnection(_dependencies.co, )
     throw new NotImplementedException();
    }

    private void InitializeDbConnection(DbConnection connection)
    {
    }


    public IDbTransaction BeginTransaction()
    {
        throw new NotImplementedException();
    }

    public IDbTransaction BeginTransaction(IsolationLevel il)
    {
        throw new NotImplementedException();
    }

    public void ChangeDatabase(string databaseName)
    {
        throw new NotImplementedException();
    }

    public void Close()
    {
        throw new NotImplementedException();
    }

    public IDbCommand CreateCommand()
    {
        throw new NotImplementedException();
    }

    public void Open()
    {
        throw new NotImplementedException();
    }

    public int ConnectionTimeout { get; }
    public string Database { get; }
    public ConnectionState State { get; }
}