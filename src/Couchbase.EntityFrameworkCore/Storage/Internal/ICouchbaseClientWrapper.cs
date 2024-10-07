namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    Task<bool> DeleteDocument(string id, string? collectionName);

    Task<bool> CreateDocument<TEntity>(string id, string collectionName, TEntity entity);

    Task<bool> UpdateDocument<TEntity>(string id, string? collectionName, TEntity entity);

    string BucketName { get; }

    string ScopeName { get; }
}