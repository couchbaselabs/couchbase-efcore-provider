using Microsoft.EntityFrameworkCore.Migrations;

namespace Couchbase.EntityFrameworkCore.Migrations.Internal;

public class CouchbaseHistoryRepository : HistoryRepository
{
    public CouchbaseHistoryRepository(HistoryRepositoryDependencies dependencies) : base(dependencies)
    {
    }

    protected override string ExistsSql { get; }
    protected override bool InterpretExistsResult(object? value)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override LockReleaseBehavior LockReleaseBehavior { get; }

    /// <summary>
    ///     Gets an exclusive lock on the database.
    /// </summary>
    /// <returns>An object that can be disposed to release the lock.</returns>
    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Gets an exclusive lock on the database.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="System.Threading.CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>An object that can be disposed to release the lock.</returns>
    /// <exception cref="System.OperationCanceledException">If the <see cref="System.Threading.CancellationToken" /> is canceled.</exception>
    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override string GetCreateIfNotExistsScript()
    {
        throw new NotImplementedException();
    }

    public override string GetBeginIfNotExistsScript(string migrationId)
    {
        throw new NotImplementedException();
    }

    public override string GetBeginIfExistsScript(string migrationId)
    {
        throw new NotImplementedException();
    }

    public override string GetEndIfScript()
    {
        throw new NotImplementedException();
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
