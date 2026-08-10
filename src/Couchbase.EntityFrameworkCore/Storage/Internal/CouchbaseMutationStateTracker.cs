using Couchbase.KeyValue;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Accumulates <see cref="IMutationResult"/>s from a single <see cref="DbContext"/> instance's own
/// writes into a <see cref="MutationState"/>, for read-your-own-writes (RYOW) queries via
/// <see cref="Couchbase.EntityFrameworkCore.Extensions.CouchbaseQueryableExtensions.ConsistentWith{TEntity}"/>.
/// Registered Scoped (one instance per <see cref="DbContext"/>, mirroring
/// <see cref="ICouchbaseClientWrapper"/>'s own lifetime) and exposed to applications via
/// <see cref="Couchbase.EntityFrameworkCore.Extensions.CouchbaseDatabaseFacadeExtensions.GetMutationState"/>/
/// <c>ClearMutationState</c>.
/// </summary>
/// <remarks>
/// Writes within a single <c>SaveChangesAsync</c> call run concurrently (see
/// <see cref="CouchbaseDatabaseWrapper"/>'s bounded-parallel write dispatch), so accumulation must
/// be thread-safe; a plain lock is more than adequate here since write batches are small and this
/// is not a hot path in the concurrent-throughput sense. State accumulates for the lifetime of the
/// owning <see cref="DbContext"/> (never auto-reset after a <c>SaveChangesAsync</c> call) --
/// deliberately mirroring Linq2Couchbase's own <c>BucketContext.MutationState</c> precedent, since
/// RYOW callers typically want every write made through the context visible, not just the most
/// recent batch. Call <c>ClearMutationState</c> explicitly to bound growth in a long-lived context.
/// </remarks>
public sealed class CouchbaseMutationStateTracker
{
    private readonly object _lock = new();
    private readonly List<IMutationResult> _results = new();

    /// <summary>
    /// Records a successful write's mutation result. Deletes have no <see cref="IMutationResult"/>
    /// to record -- see <see cref="ICouchbaseClientWrapper.DeleteDocument"/>'s remarks for why.
    /// </summary>
    public void Add(IMutationResult result)
    {
        lock (_lock)
        {
            _results.Add(result);
        }
    }

    /// <summary>
    /// Builds a fresh <see cref="MutationState"/> snapshot from every write recorded so far.
    /// </summary>
    public MutationState GetMutationState()
    {
        lock (_lock)
        {
            return MutationState.From(_results.ToArray());
        }
    }

    /// <summary>
    /// Discards all recorded writes, so the next <see cref="GetMutationState"/> call reflects only
    /// writes made afterward.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _results.Clear();
        }
    }
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
