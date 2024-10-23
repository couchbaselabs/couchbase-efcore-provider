namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    Task<bool> DeleteDocument(string id, (string? scope, string? collection) keyspace);

    Task<bool> CreateDocument<TEntity>(string id, (string? scope, string? collection) keyspace, TEntity entity);

    Task<bool> UpdateDocument<TEntity>(string id, (string? scope, string? collection) keyspace, TEntity entity);

    string BucketName { get; }
}