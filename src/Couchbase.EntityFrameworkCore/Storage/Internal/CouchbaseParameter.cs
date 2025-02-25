using System.Data;
using System.Data.Common;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseParameter : DbParameter
{
    private string _parameterName;
    private object? _value;

    public CouchbaseParameter()
    {
    }

    public CouchbaseParameter(string name, object value)
    {
        _value = value;
        _parameterName = name;
    }

    public override void ResetDbType()
    {
        throw new NotImplementedException();
    }

    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }

    public override string ParameterName
    {
        get => _parameterName;
        set => _parameterName = value;
    }

    public override string SourceColumn { get; set; }

    public override object? Value
    {
        get => _value;
        set => _value = value;
    }

    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
}