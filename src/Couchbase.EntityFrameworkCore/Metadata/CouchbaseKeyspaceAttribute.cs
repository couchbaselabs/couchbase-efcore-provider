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
    
    public CouchbaseKeyspaceAttribute(string? collection)
    {
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
    }
    
    public CouchbaseKeyspaceAttribute(string? scope, string? collection)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
    }
    
    public string? Scope { get; } = "_default";

    public string? Collection { get; } = "_default";

    public string GetKeySpace()
    {
        if (_keyspace == null)
        {
            var keyspaceBuilder = new StringBuilder();
            keyspaceBuilder.Append(Collection);
            _keyspace = keyspaceBuilder.ToString();
        }

        return _keyspace;
    }
}