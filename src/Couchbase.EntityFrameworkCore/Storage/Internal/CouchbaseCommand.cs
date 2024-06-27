using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseCommand : IRelationalCommand
{
    public DbCommand CreateDbCommand(RelationalCommandParameterObject parameterObject, Guid commandId,
        DbCommandMethod commandMethod)
    {
        throw new NotImplementedException();
    }

    public string CommandText { get; private set; }
    public IReadOnlyList<IRelationalParameter> Parameters { get; private set; }
    
    public int ExecuteNonQuery(RelationalCommandParameterObject parameterObject)
    {
        throw new NotImplementedException();
    }

    public Task<int> ExecuteNonQueryAsync(RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public object? ExecuteScalar(RelationalCommandParameterObject parameterObject)
    {
        throw new NotImplementedException();
    }

    public Task<object?> ExecuteScalarAsync(RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public RelationalDataReader ExecuteReader(RelationalCommandParameterObject parameterObject)
    {
        throw new NotImplementedException();
    }

    public Task<RelationalDataReader> ExecuteReaderAsync(RelationalCommandParameterObject parameterObject,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void PopulateFrom(IRelationalCommandTemplate commandTemplate)
    {
        CommandText = commandTemplate.CommandText;
        Parameters = commandTemplate.Parameters;
    }
}