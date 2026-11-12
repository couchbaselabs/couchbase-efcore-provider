using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// Smaller query suites: EF6-style GroupBy, unusual data, query filters, null keys, tagging,
// client-evaluation warnings, user-defined functions, and the ad hoc (one model per test) suites.

internal static class ParameterizedCollections
{
    // EF's relational option for how a parameterized collection (e.g. ids.Contains(x)) is sent:
    // as one parameter, as constants, or as one parameter per element.
    public static DbContextOptionsBuilder Set(DbContextOptionsBuilder builder, ParameterTranslationMode mode)
    {
        var extension = builder.Options.FindExtension<CouchbaseOptionsExtension>()!;
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            (CouchbaseOptionsExtension)extension.WithUseParameterizedCollectionMode(mode));
        return builder;
    }
}

// ---------- EF6 GroupBy (the Aruba model) ----------
public class Ef6GroupByCouchbaseTest(Ef6GroupByCouchbaseTest.Ef6GroupByCouchbaseFixture fixture)
    : Ef6GroupByTestBase<Ef6GroupByCouchbaseTest.Ef6GroupByCouchbaseFixture>(fixture)
{
    public class Ef6GroupByCouchbaseFixture : Ef6GroupByFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Unusual string data (wildcards, quotes, nulls) ----------
public class FunkyDataQueryCouchbaseTest(FunkyDataQueryCouchbaseTest.FunkyDataQueryCouchbaseFixture fixture)
    : FunkyDataQueryTestBase<FunkyDataQueryCouchbaseTest.FunkyDataQueryCouchbaseFixture>(fixture)
{
    public class FunkyDataQueryCouchbaseFixture : FunkyDataQueryFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Entities mapped to differently named tables (here: collections) ----------
public class MappingQueryCouchbaseTest(MappingQueryCouchbaseTest.MappingQueryCouchbaseFixture fixture)
    : MappingQueryTestBase<MappingQueryCouchbaseTest.MappingQueryCouchbaseFixture>(fixture)
{
    public class MappingQueryCouchbaseFixture : MappingQueryFixtureBase
    {
        // Couchbase has no schemas; the provider ignores them.
        protected override string DatabaseSchema => null!;

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Null and composite-null foreign keys ----------
public class NullKeysCouchbaseTest(NullKeysCouchbaseTest.NullKeysCouchbaseFixture fixture)
    : NullKeysTestBase<NullKeysCouchbaseTest.NullKeysCouchbaseFixture>(fixture)
{
    public class NullKeysCouchbaseFixture : NullKeysFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Query filters that capture context members (funcletization) ----------
public class QueryFilterFuncletizationCouchbaseFixture : QueryFilterFuncletizationRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class QueryFilterFuncletizationCouchbaseTest(QueryFilterFuncletizationCouchbaseFixture fixture)
    : QueryFilterFuncletizationTestBase<QueryFilterFuncletizationCouchbaseFixture>(fixture);

// ---------- Northwind: TagWith, client-evaluation errors, warnings ----------
public class NorthwindQueryTaggingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindQueryTaggingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class QueryNoClientEvalCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : QueryNoClientEvalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class WarningsCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : WarningsTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

// ---------- User-defined functions ----------
// EF's model maps .NET methods to database functions; SQL Server's fixture creates them in T-SQL.
// The SQL++ equivalents (CREATE FUNCTION) are not ported yet, so the tests that call a function
// fail on the missing function.
public class UdfDbFunctionCouchbaseTest(UdfDbFunctionCouchbaseTest.UdfCouchbaseFixture fixture)
    : UdfDbFunctionTestBase<UdfDbFunctionCouchbaseTest.UdfCouchbaseFixture>(fixture)
{
    public class UdfCouchbaseFixture : UdfFixtureBase
    {
        protected override string StoreName => "UdfDbFunctionTest";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Ad hoc models (a fresh model per test, one scope per suite) ----------
public class AdHocMiscellaneousQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocMiscellaneousQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocMiscellaneousQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(DbContextOptionsBuilder builder, ParameterTranslationMode mode)
        => ParameterizedCollections.Set(builder, mode);

    // SQL Server seeds this one with hand-written SQL; not ported yet.
    protected override Task Seed2951(Context2951 context)
        => throw new NotImplementedException("Raw SQL seed not ported to Couchbase (harness gap).");
}

public class AdHocNavigationsQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocNavigationsQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocNavigationsQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class AdHocQueryFiltersQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocQueryFiltersQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocQueryFiltersQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class AdHocAdvancedMappingsQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocAdvancedMappingsQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocAdvancedMappingsQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class AdHocManyToManyQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocManyToManyQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocManyToManyQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class NonSharedPrimitiveCollectionsQueryCouchbaseTest(NonSharedFixture fixture)
    : NonSharedPrimitiveCollectionsQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "NonSharedPrimitiveCollectionsQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(DbContextOptionsBuilder builder, ParameterTranslationMode mode)
        => ParameterizedCollections.Set(builder, mode);
}

public class OperatorsQueryCouchbaseTest(NonSharedFixture fixture)
    : OperatorsQueryTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "OperatorsQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class OperatorsProceduralQueryCouchbaseTest(NonSharedFixture fixture)
    : OperatorsProceduralQueryTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "OperatorsProceduralQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class SharedTypeQueryCouchbaseTest(NonSharedFixture fixture)
    : SharedTypeQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "SharedTypeQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class ToSqlQueryCouchbaseTest(NonSharedFixture fixture)
    : ToSqlQueryTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "ToSqlQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}
