using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class  CouchbaseRelationalCommandBuilder : IRelationalCommandBuilder
{
    public IReadOnlyList<IRelationalParameter> Parameters { get; }
    public IRelationalCommandBuilder AddParameter(IRelationalParameter parameter)
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder RemoveParameterAt(int index)
    {
        throw new NotImplementedException();
    }

    public IRelationalTypeMappingSource TypeMappingSource { get; }
    public IRelationalCommand Build()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder Append(string value)
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder AppendLine()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder IncrementIndent()
    {
        throw new NotImplementedException();
    }

    public IRelationalCommandBuilder DecrementIndent()
    {
        throw new NotImplementedException();
    }

    public int CommandTextLength { get; }
}