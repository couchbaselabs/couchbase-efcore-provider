using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Tier2;

// EF's transaction contract: commit, rollback, savepoints, UseTransaction with an external
// DbTransaction, AutoTransactionBehavior. Runs real transactions (no NoOpTransactionInterceptor):
// the store and its contexts share one CouchbaseConnection, as on a relational engine.
public class TransactionCouchbaseTest(TransactionCouchbaseTest.TransactionCouchbaseFixture fixture)
    : TransactionTestBase<TransactionCouchbaseTest.TransactionCouchbaseFixture>(fixture)
{
    // SQL++ transactions are read committed: no snapshot isolation level, and staged writes are
    // never visible outside the transaction.
    protected override bool SnapshotSupported => false;

    protected override bool DirtyReadsOccur => false;

    protected override DbContext CreateContextWithConnectionString()
    {
        var store = (CouchbaseRelationalTestStore)Fixture.TestStore;
        var options = Fixture.AddOptions(store.AddProviderOptionsWithOwnConnection(new DbContextOptionsBuilder()))
            .UseInternalServiceProvider(Fixture.ServiceProvider);
        return new PoolableDbContext(options.Options);
    }

    public class TransactionCouchbaseFixture : TransactionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseRelationalTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}
