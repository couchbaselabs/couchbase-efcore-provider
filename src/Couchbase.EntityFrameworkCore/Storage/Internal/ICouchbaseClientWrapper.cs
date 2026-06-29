using Couchbase.KeyValue;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    Task<bool> DeleteDocument(string id, string keyspace, CancellationToken cancellationToken = default);

    Task<bool> CreateDocument<TEntity>(string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default);

    Task<bool> UpdateDocument<TEntity>(string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default);

    string BucketName { get; }

    /// <summary>
    /// Gets the collection for the specified keyspace.
    /// </summary>
    Task<ICouchbaseCollection> GetCollectionAsync(string keyspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a document insert operation on the given transaction.
    /// </summary>
    Task EnqueueTransactionalInsert<TEntity>(CouchbaseDbTransaction transaction, string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a document upsert operation on the given transaction.
    /// </summary>
    Task EnqueueTransactionalUpsert<TEntity>(CouchbaseDbTransaction transaction, string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a document remove operation on the given transaction.
    /// </summary>
    Task EnqueueTransactionalRemove(CouchbaseDbTransaction transaction, string id, string keyspace, CancellationToken cancellationToken = default);
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
