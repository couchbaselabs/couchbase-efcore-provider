using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Creates <see cref="SingularModificationCommandBatch"/> instances for Couchbase.
/// Each batch holds exactly one DML command, matching the Couchbase SQL++ execution model.
/// </summary>
/// <remarks>
/// Mirrors <c>SqliteModificationCommandBatchFactory</c> from EF Core:
/// one command per batch lets <see cref="AffectedCountModificationCommandBatch"/> read back
/// RETURNING values and apply them to the entity before moving to the next command.
/// </remarks>
public class CouchbaseModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    public CouchbaseModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies)
        => Dependencies = dependencies;

    protected virtual ModificationCommandBatchFactoryDependencies Dependencies { get; }

    public virtual ModificationCommandBatch Create()
        => new SingularModificationCommandBatch(Dependencies);
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
