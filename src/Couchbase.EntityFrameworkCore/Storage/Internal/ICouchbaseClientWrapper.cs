using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    Task<bool> DeleteDocument<TEntity>(string id, string? contextId, TEntity entity);
    
    Task<bool> CreateDocument<TEntity>(string id, string contextId, TEntity entity);

    Task<bool> UpdateDocument<TEntity>(string id, string? contextId, TEntity entity);
}