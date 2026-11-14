using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Associations.Navigations;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// Queries across navigations between entities stored in separate documents: EF's ComplexNavigations
// model (four levels of optional and required one-to-one and one-to-many navigations), the shared
// "associations" model with plain navigations, and smaller suites for composite keys, one-to-one
// includes and optional dependents.

// ---------- ComplexNavigations ----------
public class ComplexNavigationsQueryCouchbaseFixture : ComplexNavigationsQueryRelationalFixtureBase
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

public class ComplexNavigationsQueryCouchbaseTest(ComplexNavigationsQueryCouchbaseFixture fixture)
    : ComplexNavigationsQueryRelationalTestBase<ComplexNavigationsQueryCouchbaseFixture>(fixture);

public class ComplexNavigationsCollectionsQueryCouchbaseTest(ComplexNavigationsQueryCouchbaseFixture fixture)
    : ComplexNavigationsCollectionsQueryRelationalTestBase<ComplexNavigationsQueryCouchbaseFixture>(fixture);

// The same model with its levels mapped as shared-type (property bag) entity types.
public class ComplexNavigationsSharedTypeQueryCouchbaseFixture : ComplexNavigationsSharedTypeQueryRelationalFixtureBase
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

public class ComplexNavigationsSharedTypeQueryCouchbaseTest(ComplexNavigationsSharedTypeQueryCouchbaseFixture fixture)
    : ComplexNavigationsSharedTypeQueryRelationalTestBase<ComplexNavigationsSharedTypeQueryCouchbaseFixture>(fixture);

public class ComplexNavigationsCollectionsSharedTypeQueryCouchbaseTest(ComplexNavigationsSharedTypeQueryCouchbaseFixture fixture)
    : ComplexNavigationsCollectionsSharedTypeQueryRelationalTestBase<ComplexNavigationsSharedTypeQueryCouchbaseFixture>(fixture);

// ---------- Associations matrix with plain navigations (each related entity in its own document) ----------
public class NavigationsCouchbaseFixture : NavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class NavigationsProjectionCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsProjectionRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

public class NavigationsCollectionCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsCollectionRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

public class NavigationsIncludeCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsIncludeRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

public class NavigationsMiscellaneousCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsMiscellaneousRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

public class NavigationsPrimitiveCollectionCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsPrimitiveCollectionRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

public class NavigationsSetOperationsCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsSetOperationsRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

public class NavigationsStructuralEqualityCouchbaseTest(NavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : NavigationsStructuralEqualityRelationalTestBase<NavigationsCouchbaseFixture>(fixture, output);

// ---------- Northwind Include, written with EF.Property and with include strings ----------
public class NorthwindEFPropertyIncludeQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindEFPropertyIncludeQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindStringIncludeQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindStringIncludeQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

// ---------- Composite keys (the provider builds document keys from all key parts) ----------
public class CompositeKeysQueryCouchbaseFixture : CompositeKeysQueryRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class CompositeKeysQueryCouchbaseTest(CompositeKeysQueryCouchbaseFixture fixture)
    : CompositeKeysQueryRelationalTestBase<CompositeKeysQueryCouchbaseFixture>(fixture);

// ---------- One-to-one Include ----------
public class IncludeOneToOneCouchbaseTest(IncludeOneToOneCouchbaseTest.OneToOneQueryCouchbaseFixture fixture)
    : IncludeOneToOneTestBase<IncludeOneToOneCouchbaseTest.OneToOneQueryCouchbaseFixture>(fixture)
{
    public class OneToOneQueryCouchbaseFixture : OneToOneQueryFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Optional dependents sharing their principal's row (table splitting) ----------
public class OptionalDependentQueryCouchbaseFixture : OptionalDependentQueryFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class OptionalDependentQueryCouchbaseTest(OptionalDependentQueryCouchbaseFixture fixture)
    : OptionalDependentQueryTestBase<OptionalDependentQueryCouchbaseFixture>(fixture);
