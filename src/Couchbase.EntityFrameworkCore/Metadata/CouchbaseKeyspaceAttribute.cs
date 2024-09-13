using System.Text;
using Couchbase.Core.Utils;

namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Maps an entity to a Scope and Collection. The other part of the keyspace,
/// the Bucket name, is pulled from the ClusterOptions that is injected via DI
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CouchbaseKeyspaceAttribute : Attribute
{
    private string _keyspace;
    
    public CouchbaseKeyspaceAttribute(string scope)
    {
        Scope = scope?? throw new NullReferenceException(nameof(scope));
    }
    
    public CouchbaseKeyspaceAttribute(string scope, string collection) : this(scope)
    {
        Collection = collection ?? throw new NullReferenceException(nameof(collection));
    }
    
    public string Scope { get; } = "_default";

    public string Collection { get; } = "_default";

    public string GetKeySpace()
    {
        if (_keyspace == null)
        {
            var keyspaceBuilder = new StringBuilder();
            keyspaceBuilder.Append(Scope);
            keyspaceBuilder.Append('.');
            keyspaceBuilder.Append(Collection);
            _keyspace = keyspaceBuilder.ToString();
        }

        return _keyspace;
    }
}