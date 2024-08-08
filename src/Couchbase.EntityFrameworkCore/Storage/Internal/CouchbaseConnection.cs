using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection : RelationalConnection
{
    private IDbContextTransaction? _currentTransaction;
    private IDbContextTransaction? _currentTransaction1;
    private IRelationalCommand _relationalCommand;
    private IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly RelationalConnectionDependencies _dependencies;
    private IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger;
    

    public CouchbaseConnection(RelationalConnectionDependencies dependencies, IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger, IRawSqlCommandBuilder rawSqlCommandBuilder) : base(dependencies)
    {
        _dependencies = dependencies;
        this.logger = logger;
        this._rawSqlCommandBuilder = rawSqlCommandBuilder;
    }

    protected override DbConnection CreateDbConnection()
    {
        throw new NotImplementedException();
    }
}