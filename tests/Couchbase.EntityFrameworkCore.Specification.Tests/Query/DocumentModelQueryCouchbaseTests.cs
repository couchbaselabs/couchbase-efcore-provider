using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestModels.NullSemanticsModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// Group C: how well the document model matches EF's expectations (null/missing semantics, arrays,
// complex types, TPH, raw SQL++).

// ---------- Null semantics (SQL++ NULL vs MISSING against EF's C# null semantics) ----------
public class NullSemanticsQueryCouchbaseTest(NullSemanticsQueryCouchbaseTest.NullSemanticsQueryCouchbaseFixture fixture)
    : NullSemanticsQueryTestBase<NullSemanticsQueryCouchbaseTest.NullSemanticsQueryCouchbaseFixture>(fixture)
{
    protected override NullSemanticsContext CreateContext(bool useRelationalNulls = false)
    {
        var options = new DbContextOptionsBuilder(Fixture.CreateOptions());
        if (useRelationalNulls)
        {
            var extension = options.Options.FindExtension<CouchbaseOptionsExtension>()!;
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                (CouchbaseOptionsExtension)extension.WithUseRelationalNulls(true));
        }

        var context = new NullSemanticsContext(options.Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }

    public class NullSemanticsQueryCouchbaseFixture : NullSemanticsQueryFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Primitive collections (arrays inside documents) ----------
public class PrimitiveCollectionsQueryCouchbaseTest(PrimitiveCollectionsQueryCouchbaseTest.PrimitiveCollectionsQueryCouchbaseFixture fixture)
    : PrimitiveCollectionsQueryRelationalTestBase<PrimitiveCollectionsQueryCouchbaseTest.PrimitiveCollectionsQueryCouchbaseFixture>(fixture)
{
    public class PrimitiveCollectionsQueryCouchbaseFixture : PrimitiveCollectionsQueryFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Complex types (the upstream model maps them to columns, not JSON) ----------
public class ComplexTypeQueryCouchbaseTest(ComplexTypeQueryCouchbaseTest.ComplexTypeQueryCouchbaseFixture fixture)
    : ComplexTypeQueryRelationalTestBase<ComplexTypeQueryCouchbaseTest.ComplexTypeQueryCouchbaseFixture>(fixture)
{
    public class ComplexTypeQueryCouchbaseFixture : ComplexTypeQueryRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Ad hoc complex type queries (a fresh model per test, one scope for the suite) ----------
public class AdHocComplexTypeQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocComplexTypeQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocComplexTypeQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder);
}

// ---------- TPH inheritance (the provider's discriminator model) ----------
public class TPHInheritanceQueryCouchbaseFixture : TPHInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class TPHInheritanceQueryCouchbaseTest(TPHInheritanceQueryCouchbaseFixture fixture, ITestOutputHelper output)
    : TPHInheritanceQueryTestBase<TPHInheritanceQueryCouchbaseFixture>(fixture, output)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }
}

public class TPHInheritanceBulkUpdatesCouchbaseFixture : TPHInheritanceBulkUpdatesFixture
{
    protected override string StoreName => "TPHInheritanceBulkUpdatesTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    // As in the Northwind bulk updates: real Couchbase transactions, only the cross-context join is a no-op.
    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class TPHInheritanceBulkUpdatesCouchbaseTest(TPHInheritanceBulkUpdatesCouchbaseFixture fixture, ITestOutputHelper output)
    : TPHInheritanceBulkUpdatesTestBase<TPHInheritanceBulkUpdatesCouchbaseFixture>(fixture, output);

// FromSqlQueryTestBase and SqlQueryTestBase are not wired yet; CouchbaseRelationalTestStore (used by
// TransactionCouchbaseTest) now provides the RelationalTestStore they need.
