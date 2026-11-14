using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestModels.ManyToManyModel;
using Microsoft.EntityFrameworkCore.TestModels.UpdatesModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Tier2;

// Harness notes (see NoOpTransactionInterceptor): suites that isolate tests with a shared, rolled-back
// transaction run with no-op transactions and reseed the store between tests instead.

// ---------- Optimistic concurrency (F1 model, row versions mapped to Couchbase CAS) ----------
public class F1CouchbaseFixture : F1RelationalFixture<ulong>
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
    public override TestHelpers TestHelpers => CouchbaseTestHelpers.Instance;

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, mapRowVersionsToCas: true);
    }
}

public class OptimisticConcurrencyCouchbaseTest(F1CouchbaseFixture fixture)
    : OptimisticConcurrencyRelationalTestBase<F1CouchbaseFixture, ulong>(fixture), Xunit.IAsyncLifetime
{
    // xUnit runs this before every test (the class is constructed per test).
    public Task InitializeAsync() => Fixture.ReseedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }
}

// ---------- Updates ----------
public class UpdatesCouchbaseTest(UpdatesCouchbaseTest.UpdatesCouchbaseFixture fixture)
    : UpdatesRelationalTestBase<UpdatesCouchbaseTest.UpdatesCouchbaseFixture>(fixture)
{
    public override void Identifiers_are_generated_correctly() { }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(
        Func<UpdatesContext, Task> testOperation,
        Func<UpdatesContext, Task>? nestedTestOperation1 = null,
        Func<UpdatesContext, Task>? nestedTestOperation2 = null)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation, nestedTestOperation1, nestedTestOperation2); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class UpdatesCouchbaseFixture : UpdatesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Store-generated values and fixup ----------
public class StoreGeneratedFixupCouchbaseTest(StoreGeneratedFixupCouchbaseTest.StoreGeneratedFixupCouchbaseFixture fixture)
    : StoreGeneratedFixupRelationalTestBase<StoreGeneratedFixupCouchbaseTest.StoreGeneratedFixupCouchbaseFixture>(fixture)
{
    protected override bool EnforcesFKs => false;

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(Func<DbContext, Task> testOperation)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class StoreGeneratedFixupCouchbaseFixture : StoreGeneratedFixupRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Keys with value converters ----------
public class KeysWithConvertersCouchbaseTest(KeysWithConvertersCouchbaseTest.KeysWithConvertersCouchbaseFixture fixture)
    : KeysWithConvertersTestBase<KeysWithConvertersCouchbaseTest.KeysWithConvertersCouchbaseFixture>(fixture)
{
    public class KeysWithConvertersCouchbaseFixture : KeysWithConvertersFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(CoreEventId.CollectionWithoutComparer));

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Find (blocked by the shadow-key bug; kept so it shows up when fixed) ----------
public abstract class FindCouchbaseTest(FindCouchbaseTest.FindCouchbaseFixture fixture)
    : FindTestBase<FindCouchbaseTest.FindCouchbaseFixture>(fixture)
{
    public class FindCouchbaseTestSet(FindCouchbaseFixture fixture) : FindCouchbaseTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaSetFinder();
    }

    public class FindCouchbaseTestContext(FindCouchbaseFixture fixture) : FindCouchbaseTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaContextFinder();
    }

    public class FindCouchbaseTestNonGeneric(FindCouchbaseFixture fixture) : FindCouchbaseTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaNonGenericContextFinder();
    }

    public class FindCouchbaseFixture : FindFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Many-to-many tracking ----------
public class ManyToManyTrackingCouchbaseTest(ManyToManyTrackingCouchbaseTest.ManyToManyTrackingCouchbaseFixture fixture)
    : ManyToManyTrackingRelationalTestBase<ManyToManyTrackingCouchbaseTest.ManyToManyTrackingCouchbaseFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(
        Func<ManyToManyContext, Task> testOperation,
        Func<ManyToManyContext, Task>? nestedTestOperation1 = null,
        Func<ManyToManyContext, Task>? nestedTestOperation2 = null,
        Func<ManyToManyContext, Task>? nestedTestOperation3 = null)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation, nestedTestOperation1, nestedTestOperation2, nestedTestOperation3); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class ManyToManyTrackingCouchbaseFixture : ManyToManyTrackingRelationalFixture, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Bulk updates: real Couchbase transactions roll ExecuteUpdate/Delete back; only the
// cross-context join is a no-op. ----------
public class NorthwindBulkUpdatesCouchbaseFixture<TModelCustomizer> : NorthwindBulkUpdatesRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class NorthwindBulkUpdatesCouchbaseTest(NorthwindBulkUpdatesCouchbaseFixture<NoopModelCustomizer> fixture, Xunit.Abstractions.ITestOutputHelper output)
    : NorthwindBulkUpdatesRelationalTestBase<NorthwindBulkUpdatesCouchbaseFixture<NoopModelCustomizer>>(fixture, output);
