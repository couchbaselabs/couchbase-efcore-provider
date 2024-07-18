using System.Text;
using Couchbase.Core.Utils;

namespace Couchbase.EntityFrameworkCore.Metadata;

[AttributeUsage(AttributeTargets.Class)]
public class CouchbaseKeyspaceAttribute : Attribute
{
    private string _contextId;
    
    public CouchbaseKeyspaceAttribute(string bucket)
    {
        Bucket = bucket ?? throw new NullReferenceException(nameof(bucket));
    }
    
    public CouchbaseKeyspaceAttribute(string bucket, string scope) : this(bucket)
    {
        Scope = scope?? throw new NullReferenceException(nameof(scope));
    }
    
    public CouchbaseKeyspaceAttribute(string bucket, string scope, string collection) : this(bucket, scope)
    {
        Collection = collection ?? throw new NullReferenceException(nameof(collection));
    }
    
    public string Bucket { get; }

    public string Scope { get; } = "_default";

    public string Collection { get; } = "_default";

    public string GetKeySpace()
    {
        if (_contextId == null)
        {
            var contextBuilder = new StringBuilder();
            contextBuilder.Append(Bucket);
            contextBuilder.Append('.');
            contextBuilder.Append(Scope);
            contextBuilder.Append('.');
            contextBuilder.Append(Collection);
            _contextId = contextBuilder.ToString();
        }

        return _contextId;
    }
}