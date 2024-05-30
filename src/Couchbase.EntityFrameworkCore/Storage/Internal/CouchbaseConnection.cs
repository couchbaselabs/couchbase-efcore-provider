using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseConnection : IDbConnection
{
    public void Dispose()
    {
        throw new NotImplementedException();
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

    public string ConnectionString { get; set; }
    public int ConnectionTimeout { get; }
    public string Database { get; }
    public ConnectionState State { get; }
}