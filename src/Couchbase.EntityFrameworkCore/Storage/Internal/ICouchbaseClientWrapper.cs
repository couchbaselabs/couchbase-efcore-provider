using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    Task<bool> DeleteDocument(string id, string? scopeAndCollection);
    
    Task<bool> CreateDocument<TEntity>(string id, string scopeAndCollection, TEntity entity);

    Task<bool> UpdateDocument<TEntity>(string id, string? scopeAndCollection, TEntity entity);
    
    string BucketName { get; }
}