using System.Reflection;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// ---------- Split queries (AsSplitQuery: one query per included collection) ----------
public class NorthwindSplitIncludeQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindSplitIncludeQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindSplitIncludeNoTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindSplitIncludeNoTrackingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class ComplexNavigationsCollectionsSplitQueryCouchbaseTest(ComplexNavigationsQueryCouchbaseFixture fixture)
    : ComplexNavigationsCollectionsSplitQueryRelationalTestBase<ComplexNavigationsQueryCouchbaseFixture>(fixture);

public class ComplexNavigationsCollectionsSplitSharedTypeQueryCouchbaseTest(ComplexNavigationsSharedTypeQueryCouchbaseFixture fixture)
    : ComplexNavigationsCollectionsSplitSharedTypeQueryRelationalTestBase<ComplexNavigationsSharedTypeQueryCouchbaseFixture>(fixture);

public class CompositeKeysSplitQueryCouchbaseTest(CompositeKeysQueryCouchbaseFixture fixture)
    : CompositeKeysSplitQueryRelationalTestBase<CompositeKeysQueryCouchbaseFixture>(fixture);

public class AdHocQuerySplittingQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocQuerySplittingQueryTestBase(fixture), IClassFixture<NonSharedFixture>
{
    // EF's relational options keep the configured behavior in a private field; SQL Server's own
    // test clears it the same way.
    private static readonly FieldInfo QuerySplittingBehaviorField
        = typeof(RelationalOptionsExtension).GetField("_querySplittingBehavior", BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected override string StoreName => "AdHocQuerySplittingQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override DbContextOptionsBuilder SetQuerySplittingBehavior(DbContextOptionsBuilder optionsBuilder, QuerySplittingBehavior splittingBehavior)
    {
        var extension = optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension>()!;
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            (CouchbaseOptionsExtension)extension.WithUseQuerySplittingBehavior(splittingBehavior));
        return optionsBuilder;
    }

    protected override DbContextOptionsBuilder ClearQuerySplittingBehavior(DbContextOptionsBuilder optionsBuilder)
    {
        var extension = optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension>();
        if (extension != null)
        {
            QuerySplittingBehaviorField.SetValue(extension, null);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        }

        return optionsBuilder;
    }
}

// ---------- Lazy loading and change tracking through EF's proxies ----------
public class LazyLoadProxyCouchbaseTest(LazyLoadProxyCouchbaseTest.LoadCouchbaseFixture fixture)
    : LazyLoadProxyRelationalTestBase<LazyLoadProxyCouchbaseTest.LoadCouchbaseFixture>(fixture)
{
    public class LoadCouchbaseFixture : LoadRelationalFixtureBase
    {
        protected override string StoreName => "LazyLoadProxyTest";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

public class ProxyGraphUpdatesWithLazyLoadingCouchbaseTest(ProxyGraphUpdatesWithLazyLoadingCouchbaseTest.ProxyGraphUpdatesCouchbaseFixture fixture)
    : ProxyGraphUpdatesTestBase<ProxyGraphUpdatesWithLazyLoadingCouchbaseTest.ProxyGraphUpdatesCouchbaseFixture>(fixture)
{
    protected override bool DoesLazyLoading => true;

    protected override bool DoesChangeTracking => false;

    public class ProxyGraphUpdatesCouchbaseFixture : ProxyGraphUpdatesFixtureBase
    {
        protected override string StoreName => "ProxyGraphLazyLoadingUpdatesTest";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder.UseLazyLoadingProxies())
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}
