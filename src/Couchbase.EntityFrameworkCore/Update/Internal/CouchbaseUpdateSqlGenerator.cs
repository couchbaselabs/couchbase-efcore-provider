using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Update.Internal;

public class CouchbaseUpdateSqlGenerator : IUpdateSqlGenerator
{
    public string GenerateNextSequenceValueOperation(string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public void AppendNextSequenceValueOperation(StringBuilder commandStringBuilder, string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public string GenerateObtainNextSequenceValueOperation(string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public void AppendObtainNextSequenceValueOperation(StringBuilder commandStringBuilder, string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public void AppendBatchHeader(StringBuilder commandStringBuilder)
    {
        throw new NotImplementedException();
    }

    public void PrependEnsureAutocommit(StringBuilder commandStringBuilder)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendDeleteOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendInsertOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendUpdateOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendStoredProcedureCall(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }
}