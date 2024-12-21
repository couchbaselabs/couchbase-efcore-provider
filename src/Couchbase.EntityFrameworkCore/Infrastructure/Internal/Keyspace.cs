using Couchbase.Core.Utils;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public interface IKeyspace
{
    public string BucketName { get; }
    
    public string ScopeName { get; }
    
    public string GetKeyspace(string collectionName);
}

internal class Keyspace : IKeyspace
{
    public Keyspace(string bucketName, string scopeName)
    {
        BucketName = bucketName;
        ScopeName = scopeName;
    }

    public string BucketName { get; }
    public string ScopeName { get; }
    
    public string GetKeyspace(string collectionName)
    {
        return BucketName.EscapeIfRequired() + "." + 
               ScopeName.EscapeIfRequired()+ "." + 
               collectionName.EscapeIfRequired();
    }
}