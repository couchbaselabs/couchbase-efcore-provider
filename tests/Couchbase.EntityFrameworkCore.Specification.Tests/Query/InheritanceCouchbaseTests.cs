using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// Inheritance suites beyond TPHInheritanceQuery and the TPC suites in TpcQueryCouchbaseTests:
// query filters on a hierarchy, relationships between hierarchies, many-to-many between
// hierarchies, and bulk updates. Each runs for TPH (the default) and for TPC. EF's TPT variants
// are not wired: the provider does not support TPT.

// ---------- Query filters on an inheritance hierarchy ----------
public class TPHFiltersInheritanceQueryCouchbaseFixture : TPHInheritanceQueryCouchbaseFixture
{
    protected override string StoreName => "TPHFiltersInheritanceQueryTest";

    public override bool EnableFilters => true;
}

public class TPHFiltersInheritanceQueryCouchbaseTest(TPHFiltersInheritanceQueryCouchbaseFixture fixture)
    : FiltersInheritanceQueryTestBase<TPHFiltersInheritanceQueryCouchbaseFixture>(fixture);

public class TPCFiltersInheritanceQueryCouchbaseFixture : TPCInheritanceQueryCouchbaseFixture
{
    protected override string StoreName => "TPCFiltersInheritanceQueryTest";

    public override bool EnableFilters => true;
}

public class TPCFiltersInheritanceQueryCouchbaseTest(TPCFiltersInheritanceQueryCouchbaseFixture fixture)
    : TPCFiltersInheritanceQueryTestBase<TPCFiltersInheritanceQueryCouchbaseFixture>(fixture);

// ---------- Relationships between inheritance hierarchies ----------
public class InheritanceRelationshipsQueryCouchbaseFixture : InheritanceRelationshipsQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class InheritanceRelationshipsQueryCouchbaseTest(InheritanceRelationshipsQueryCouchbaseFixture fixture)
    : InheritanceRelationshipsQueryRelationalTestBase<InheritanceRelationshipsQueryCouchbaseFixture>(fixture);

public class TPCRelationshipsQueryCouchbaseFixture : TPCRelationshipsQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w
            .Ignore(RelationalEventId.MultipleCollectionIncludeWarning)
            .Ignore(RelationalEventId.TpcStoreGeneratedIdentityWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class TPCRelationshipsQueryCouchbaseTest(TPCRelationshipsQueryCouchbaseFixture fixture)
    : TPCRelationshipsQueryTestBase<TPCRelationshipsQueryCouchbaseFixture>(fixture);

// ---------- Many-to-many between inheritance hierarchies (skip navigations, join entities) ----------
public class ManyToManyQueryCouchbaseFixture : ManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class ManyToManyQueryCouchbaseTest(ManyToManyQueryCouchbaseFixture fixture)
    : ManyToManyQueryRelationalTestBase<ManyToManyQueryCouchbaseFixture>(fixture);

public class ManyToManyNoTrackingQueryCouchbaseTest(ManyToManyQueryCouchbaseFixture fixture)
    : ManyToManyNoTrackingQueryRelationalTestBase<ManyToManyQueryCouchbaseFixture>(fixture);

public class TPCManyToManyQueryCouchbaseFixture : TPCManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w
            .Ignore(RelationalEventId.MultipleCollectionIncludeWarning)
            .Ignore(RelationalEventId.TpcStoreGeneratedIdentityWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class TPCManyToManyQueryCouchbaseTest(TPCManyToManyQueryCouchbaseFixture fixture)
    : TPCManyToManyQueryRelationalTestBase<TPCManyToManyQueryCouchbaseFixture>(fixture);

public class TPCManyToManyNoTrackingQueryCouchbaseTest(TPCManyToManyQueryCouchbaseFixture fixture)
    : TPCManyToManyNoTrackingQueryRelationalTestBase<TPCManyToManyQueryCouchbaseFixture>(fixture);

// ---------- Bulk updates on inheritance hierarchies ----------
// Each fixture has its own scope: ExecuteUpdate/ExecuteDelete write, and other suites share none of it.
public class TPHFiltersInheritanceBulkUpdatesCouchbaseFixture : TPHInheritanceBulkUpdatesCouchbaseFixture
{
    protected override string StoreName => "TPHFiltersInheritanceBulkUpdatesTest";

    public override bool EnableFilters => true;
}

public class TPHFiltersInheritanceBulkUpdatesCouchbaseTest(TPHFiltersInheritanceBulkUpdatesCouchbaseFixture fixture, ITestOutputHelper output)
    : FiltersInheritanceBulkUpdatesRelationalTestBase<TPHFiltersInheritanceBulkUpdatesCouchbaseFixture>(fixture, output)
{
    protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();
}

public class TPCInheritanceBulkUpdatesCouchbaseFixture : TPCInheritanceBulkUpdatesFixture
{
    protected override string StoreName => "TPCInheritanceBulkUpdatesTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.TpcStoreGeneratedIdentityWarning));

    // As for the TPH bulk updates: only the cross-context transaction join is a no-op.
    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class TPCInheritanceBulkUpdatesCouchbaseTest(TPCInheritanceBulkUpdatesCouchbaseFixture fixture, ITestOutputHelper output)
    : TPCInheritanceBulkUpdatesTestBase<TPCInheritanceBulkUpdatesCouchbaseFixture>(fixture, output);

public class TPCFiltersInheritanceBulkUpdatesCouchbaseFixture : TPCInheritanceBulkUpdatesCouchbaseFixture
{
    protected override string StoreName => "TPCFiltersInheritanceBulkUpdatesTest";

    public override bool EnableFilters => true;
}

public class TPCFiltersInheritanceBulkUpdatesCouchbaseTest(TPCFiltersInheritanceBulkUpdatesCouchbaseFixture fixture, ITestOutputHelper output)
    : TPCFiltersInheritanceBulkUpdatesTestBase<TPCFiltersInheritanceBulkUpdatesCouchbaseFixture>(fixture, output)
{
    protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();
}
