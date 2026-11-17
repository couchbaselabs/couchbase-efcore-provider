using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// Test-harness only. EF's spec suites isolate tests by beginning a transaction, sharing it with
/// nested contexts (UseTransaction) and rolling back. Each Couchbase context has its own connection, so
/// that sharing is rejected. Like the InMemory/Cosmos spec suites, this turns the per-test transaction
/// into a no-op (writes commit) and the fixture reseeds the store between tests instead.
/// </summary>
public sealed class NoOpTransactionInterceptor : DbTransactionInterceptor
{
    public static NoOpTransactionInterceptor Instance { get; } = new();

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        => InterceptionResult<DbTransaction>.SuppressWithResult(new NoOpDbTransaction(connection));

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(InterceptionResult<DbTransaction>.SuppressWithResult(new NoOpDbTransaction(connection)));

    private sealed class NoOpDbTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }
}
