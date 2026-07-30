using Couchbase.KeyValue;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public interface ICouchbaseClientWrapper
{
    /// <summary>
    /// Deletes a document. If <paramref name="cas"/> is supplied, the delete is conditioned on it
    /// (via the SDK's <c>RemoveOptions.Cas</c>) and throws
    /// <see cref="Couchbase.Core.Exceptions.CasMismatchException"/> if the document was modified
    /// since that CAS was read; when <see langword="null"/>, the delete is unconditional.
    /// </summary>
    Task<bool> DeleteDocument(string id, string keyspace, ulong? cas = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new document and returns its resulting CAS.
    /// </summary>
    Task<ulong> CreateDocument<TEntity>(string id, string keyspace, TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a document and returns its resulting CAS. If <paramref name="cas"/> is supplied, this
    /// uses a CAS-checked replace (via the SDK's <c>ReplaceOptions.Cas</c>) and throws
    /// <see cref="Couchbase.Core.Exceptions.CasMismatchException"/> if the document was modified
    /// since that CAS was read; when <see langword="null"/>, this is an unconditional upsert
    /// (create-or-replace), matching this method's original behavior.
    /// </summary>
    Task<ulong> UpdateDocument<TEntity>(string id, string keyspace, TEntity entity, ulong? cas = null, CancellationToken cancellationToken = default);

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
