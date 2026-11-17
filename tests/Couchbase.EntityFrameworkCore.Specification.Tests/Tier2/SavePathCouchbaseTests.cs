using Couchbase.EntityFrameworkCore.SpecificationTests.Infrastructure;
using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Tier2;

// Group A: suites that exercise the save path (change tracking -> documents written).
// Same harness as the other Tier 2 suites: suites that isolate tests with a shared, rolled-back
// transaction run with no-op transactions (NoOpTransactionInterceptor) and reseed between tests.

// ---------- Graph updates (Added/Modified/Deleted graphs, fixup, cascades) ----------
public class GraphUpdatesCouchbaseTest(GraphUpdatesCouchbaseTest.GraphUpdatesCouchbaseFixture fixture)
    : GraphUpdatesTestBase<GraphUpdatesCouchbaseTest.GraphUpdatesCouchbaseFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(
        Func<DbContext, Task> testOperation,
        Func<DbContext, Task>? nestedTestOperation1 = null,
        Func<DbContext, Task>? nestedTestOperation2 = null,
        Func<DbContext, Task>? nestedTestOperation3 = null)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation, nestedTestOperation1, nestedTestOperation2, nestedTestOperation3); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class GraphUpdatesCouchbaseFixture : GraphUpdatesFixtureBase
    {
        protected override string StoreName => "GraphUpdatesTest";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder)
                .AddInterceptors(NoOpTransactionInterceptor.Instance)
                // Graph queries Include several collections; EF's test defaults turn this
                // performance warning into an error, which would fail nearly every test.
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Complex types change tracking ----------
public class ComplexTypesTrackingCouchbaseTest(ComplexTypesTrackingCouchbaseTest.ComplexTypesTrackingCouchbaseFixture fixture, ITestOutputHelper output)
    : ComplexTypesTrackingRelationalTestBase<ComplexTypesTrackingCouchbaseTest.ComplexTypesTrackingCouchbaseFixture>(fixture, output)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(
        Func<DbContext, Task> testOperation,
        Func<DbContext, Task>? nestedTestOperation1 = null,
        Func<DbContext, Task>? nestedTestOperation2 = null,
        Func<DbContext, Task>? nestedTestOperation3 = null)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation, nestedTestOperation1, nestedTestOperation2, nestedTestOperation3); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class ComplexTypesTrackingCouchbaseFixture : RelationalFixtureBase
    {
        protected override string StoreName => "ComplexTypesTrackingTest";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Property values (current/original/database values) ----------
public class PropertyValuesCouchbaseTest(PropertyValuesCouchbaseTest.PropertyValuesCouchbaseFixture fixture)
    : PropertyValuesRelationalTestBase<PropertyValuesCouchbaseTest.PropertyValuesCouchbaseFixture>(fixture)
{
    public class PropertyValuesCouchbaseFixture : PropertyValuesRelationalFixture
    {
        protected override string StoreName => "PropertyValues";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- SaveChanges interception (the provider's own save interceptor runs alongside) ----------
public class SaveChangesInterceptionCouchbaseTest(SaveChangesInterceptionCouchbaseTest.InterceptionCouchbaseFixture fixture)
    : SaveChangesInterceptionTestBase(fixture),
        IClassFixture<SaveChangesInterceptionCouchbaseTest.InterceptionCouchbaseFixture>
{
    public class InterceptionCouchbaseFixture : InterceptionFixtureBase
    {
        protected override string StoreName => "SaveChangesInterception";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override bool ShouldSubscribeToDiagnosticListener => false;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(
                SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName).AddProviderServices(serviceCollection),
                injectedInterceptors);
    }
}

// ---------- Store value generation (which values come back from the store, per operation) ----------
public class StoreValueGenerationCouchbaseTest(StoreValueGenerationCouchbaseTest.StoreValueGenerationCouchbaseFixture fixture)
    : StoreValueGenerationTestBase<StoreValueGenerationCouchbaseTest.StoreValueGenerationCouchbaseFixture>(fixture), IAsyncLifetime
{
    public Task InitializeAsync() => Fixture.ReseedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public class StoreValueGenerationCouchbaseFixture : StoreValueGenerationFixtureBase
    {
        protected override string StoreName => "StoreValueGenerationTest";

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

// ---------- Store-generated values (defaults, computed, identity, concurrency tokens) ----------
public class StoreGeneratedCouchbaseTest(StoreGeneratedCouchbaseTest.StoreGeneratedCouchbaseFixture fixture)
    : StoreGeneratedTestBase<StoreGeneratedCouchbaseTest.StoreGeneratedCouchbaseFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(
        Func<DbContext, Task> testOperation,
        Func<DbContext, Task>? nestedTestOperation1 = null,
        Func<DbContext, Task>? nestedTestOperation2 = null,
        Func<DbContext, Task>? nestedTestOperation3 = null)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation, nestedTestOperation1, nestedTestOperation2, nestedTestOperation3); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class StoreGeneratedCouchbaseFixture : StoreGeneratedFixtureBase
    {
        protected override string StoreName => "StoreGeneratedTest";

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

// ---------- Data annotations ([Key], [ConcurrencyCheck], [Required], ...) ----------
public class DataAnnotationCouchbaseTest(DataAnnotationCouchbaseTest.DataAnnotationCouchbaseFixture fixture)
    : DataAnnotationRelationalTestBase<DataAnnotationCouchbaseTest.DataAnnotationCouchbaseFixture>(fixture)
{
    protected override TestHelpers TestHelpers => CouchbaseTestHelpers.Instance;

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override async Task ExecuteWithStrategyInTransactionAsync(Func<DbContext, Task> testOperation)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation); }
        finally { await Fixture.ReseedAsync(); }
    }

    protected override async Task ExecuteWithStrategyInTransactionAsync(
        Func<DbContext, Task> testOperation, Func<DbContext, Task> nestedTestOperation)
    {
        try { await base.ExecuteWithStrategyInTransactionAsync(testOperation, nestedTestOperation); }
        finally { await Fixture.ReseedAsync(); }
    }

    public class DataAnnotationCouchbaseFixture : DataAnnotationRelationalFixtureBase
    {
        protected override string StoreName => "DataAnnotations";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}
