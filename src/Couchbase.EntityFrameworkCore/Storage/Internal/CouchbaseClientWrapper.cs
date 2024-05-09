namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseClientWrapper : ICouchbaseClientWrapper
{
    public Task<bool> CreateDocument<TEntity>(string id, TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> UpdateDocument<TEntity>(string id, TEntity entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteDocument(string id)
    {
        throw new NotImplementedException();
    }

    public Task<TEntity> SelectDocument<TEntity>(string id)
    {
        throw new NotImplementedException();
    }
}