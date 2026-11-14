using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Associations;

// EF's shared "associations" matrix, run with complex types mapped to JSON (ComplexProperty /
// ComplexCollection + ToJson): embedded objects and arrays inside the owning document.
public class ComplexJsonCouchbaseFixture : ComplexJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    // Bulk updates run in a real Couchbase transaction that rolls back; only the cross-context
    // join (unsupported across Couchbase connections) is a no-op.
    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class ComplexJsonProjectionCouchbaseTest(ComplexJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonProjectionRelationalTestBase<ComplexJsonCouchbaseFixture>(fixture, output);

public class ComplexJsonCollectionCouchbaseTest(ComplexJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonCollectionRelationalTestBase<ComplexJsonCouchbaseFixture>(fixture, output);

public class ComplexJsonMiscellaneousCouchbaseTest(ComplexJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonMiscellaneousRelationalTestBase<ComplexJsonCouchbaseFixture>(fixture, output);

public class ComplexJsonPrimitiveCollectionCouchbaseTest(ComplexJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonPrimitiveCollectionRelationalTestBase<ComplexJsonCouchbaseFixture>(fixture, output);

public class ComplexJsonSetOperationsCouchbaseTest(ComplexJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonSetOperationsRelationalTestBase<ComplexJsonCouchbaseFixture>(fixture, output);

public class ComplexJsonStructuralEqualityCouchbaseTest(ComplexJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonStructuralEqualityRelationalTestBase<ComplexJsonCouchbaseFixture>(fixture, output);

// Own store: ExecuteDelete/ExecuteUpdate inside a Couchbase transaction are not rolled back
// (a provider bug found by this suite), so bulk tests must not share data with the query suites.
public class ComplexJsonBulkUpdateCouchbaseFixture : ComplexJsonCouchbaseFixture
{
    protected override string StoreName => "ComplexJsonBulkUpdate";
}

public class ComplexJsonBulkUpdateCouchbaseTest(ComplexJsonBulkUpdateCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexJsonBulkUpdateRelationalTestBase<ComplexJsonBulkUpdateCouchbaseFixture>(fixture, output);

public class ComplexCollectionJsonUpdateCouchbaseTest(ComplexCollectionJsonUpdateCouchbaseTest.ComplexCollectionJsonUpdateCouchbaseFixture fixture)
    : ComplexCollectionJsonUpdateTestBase<ComplexCollectionJsonUpdateCouchbaseTest.ComplexCollectionJsonUpdateCouchbaseFixture>(fixture),
        Xunit.IAsyncLifetime
{
    // No-op transactions (writes commit) + a reseed before every test, as in Tier 2.
    public Task InitializeAsync() => fixture.ReseedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    public class ComplexCollectionJsonUpdateCouchbaseFixture : ComplexCollectionJsonUpdateFixtureBase
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
