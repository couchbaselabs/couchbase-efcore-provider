namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    Task<bool> CreateDocument<TEntity>(string id, TEntity entity);

    Task<bool> UpdateDocument<TEntity>(string id, TEntity entity);

    Task<bool> DeleteDocument(string id);

    Task<TEntity> SelectDocument<TEntity>(string id);
}