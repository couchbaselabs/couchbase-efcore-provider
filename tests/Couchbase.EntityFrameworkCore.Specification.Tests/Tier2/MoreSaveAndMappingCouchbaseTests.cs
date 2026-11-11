using System.Reflection;
using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestModels;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Tier2;

// Loading, mapping, value conversion, interception and end-to-end model suites.

// ---------- Explicit loading (Entry(...).Reference/Collection(...).Load) ----------
public class LoadCouchbaseTest(LoadCouchbaseTest.LoadCouchbaseFixture fixture)
    : LoadTestBase<LoadCouchbaseTest.LoadCouchbaseFixture>(fixture)
{
    public class LoadCouchbaseFixture : LoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

public class FieldsOnlyLoadCouchbaseTest(FieldsOnlyLoadCouchbaseTest.FieldsOnlyLoadCouchbaseFixture fixture)
    : FieldsOnlyLoadTestBase<FieldsOnlyLoadCouchbaseTest.FieldsOnlyLoadCouchbaseFixture>(fixture)
{
    public class FieldsOnlyLoadCouchbaseFixture : FieldsOnlyLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

public class ManyToManyLoadCouchbaseTest(ManyToManyLoadCouchbaseTest.ManyToManyLoadCouchbaseFixture fixture)
    : ManyToManyLoadTestBase<ManyToManyLoadCouchbaseTest.ManyToManyLoadCouchbaseFixture>(fixture)
{
    public class ManyToManyLoadCouchbaseFixture : ManyToManyLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

public class ManyToManyFieldsLoadCouchbaseTest(ManyToManyFieldsLoadCouchbaseTest.ManyToManyFieldsLoadCouchbaseFixture fixture)
    : ManyToManyFieldsLoadTestBase<ManyToManyFieldsLoadCouchbaseTest.ManyToManyFieldsLoadCouchbaseFixture>(fixture)
{
    public class ManyToManyFieldsLoadCouchbaseFixture : ManyToManyFieldsLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Field mapping and constructor binding ----------
public class FieldMappingCouchbaseTest(FieldMappingCouchbaseTest.FieldMappingCouchbaseFixture fixture)
    : FieldMappingTestBase<FieldMappingCouchbaseTest.FieldMappingCouchbaseFixture>(fixture)
{
    public class FieldMappingCouchbaseFixture : FieldMappingFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

public class WithConstructorsCouchbaseTest(WithConstructorsCouchbaseTest.WithConstructorsCouchbaseFixture fixture)
    : WithConstructorsTestBase<WithConstructorsCouchbaseTest.WithConstructorsCouchbaseFixture>(fixture)
{
    public class WithConstructorsCouchbaseFixture : WithConstructorsFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Value conversion: provider types and custom converters ----------
public class ConvertToProviderTypesCouchbaseTest(ConvertToProviderTypesCouchbaseTest.ConvertToProviderTypesCouchbaseFixture fixture)
    : ConvertToProviderTypesTestBase<ConvertToProviderTypesCouchbaseTest.ConvertToProviderTypesCouchbaseFixture>(fixture)
{
    public class ConvertToProviderTypesCouchbaseFixture : ConvertToProviderTypesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        // Same capabilities as BuiltInDataTypesCouchbaseTest.
        public override bool StrictEquality => false;
        public override bool SupportsAnsi => false;
        public override bool SupportsUnicodeToAnsiConversion => true;
        public override bool SupportsLargeStringComparisons => true;
        public override bool SupportsBinaryKeys => false;
        public override bool SupportsDecimalComparisons => false;
        public override DateTime DefaultDateTime => new();
        public override bool PreservesDateTimeKind => false;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

public class CustomConvertersCouchbaseTest(CustomConvertersCouchbaseTest.CustomConvertersCouchbaseFixture fixture)
    : CustomConvertersTestBase<CustomConvertersCouchbaseTest.CustomConvertersCouchbaseFixture>(fixture)
{
    public class CustomConvertersCouchbaseFixture : CustomConvertersFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override bool StrictEquality => false;
        public override bool SupportsAnsi => false;
        public override bool SupportsUnicodeToAnsiConversion => true;
        public override bool SupportsLargeStringComparisons => true;
        public override bool SupportsBinaryKeys => false;
        public override bool SupportsDecimalComparisons => false;
        public override DateTime DefaultDateTime => new();
        public override bool PreservesDateTimeKind => false;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- The F1 model with byte[] row versions (data binding, serialization of graphs) ----------
// Couchbase row versions are CAS values (ulong; see F1CouchbaseFixture). These suites are written
// for byte[] row versions, which the provider does not generate.
public class F1BytesCouchbaseFixture : F1RelationalFixture<byte[]>
{
    protected override string StoreName => "F1BytesTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override TestHelpers TestHelpers => CouchbaseTestHelpers.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class DataBindingCouchbaseTest(F1BytesCouchbaseFixture fixture)
    : DataBindingTestBase<F1BytesCouchbaseFixture>(fixture);

public class SerializationCouchbaseTest(F1BytesCouchbaseFixture fixture)
    : SerializationTestBase<F1BytesCouchbaseFixture>(fixture);

// ---------- Change tracking across a large model ----------
public class MonsterFixupSnapshotCouchbaseTest(MonsterFixupSnapshotCouchbaseTest.MonsterFixupSnapshotCouchbaseFixture fixture)
    : MonsterFixupTestBase<MonsterFixupSnapshotCouchbaseTest.MonsterFixupSnapshotCouchbaseFixture>(fixture)
{
    public class MonsterFixupSnapshotCouchbaseFixture : MonsterFixupFixtureBase
    {
        public override string StoreName => "MonsterFixupSnapshotTest";

        public override bool UseDetectChanges => true;

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override MonsterContext CreateContext(DbContextOptions options) => new SnapshotMonsterContext(options);

        protected override void OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(
            ModelBuilder builder)
        {
            base.OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(builder);
            CouchbaseSpecModel.Apply(builder, null!);
        }
    }
}

// ---------- End-to-end sample models ----------
public class ConferencePlannerCouchbaseTest(ConferencePlannerCouchbaseTest.ConferencePlannerCouchbaseFixture fixture)
    : ConferencePlannerTestBase<ConferencePlannerCouchbaseTest.ConferencePlannerCouchbaseFixture>(fixture)
{
    public class ConferencePlannerCouchbaseFixture : ConferencePlannerFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

public class MusicStoreCouchbaseTest(MusicStoreCouchbaseTest.MusicStoreCouchbaseFixture fixture)
    : MusicStoreTestBase<MusicStoreCouchbaseTest.MusicStoreCouchbaseFixture>(fixture)
{
    public class MusicStoreCouchbaseFixture : MusicStoreFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

public class CompositeKeyEndToEndCouchbaseTest(CompositeKeyEndToEndCouchbaseTest.CompositeKeyEndToEndCouchbaseFixture fixture)
    : CompositeKeyEndToEndTestBase<CompositeKeyEndToEndCouchbaseTest.CompositeKeyEndToEndCouchbaseFixture>(fixture)
{
    public class CompositeKeyEndToEndCouchbaseFixture : CompositeKeyEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

public class NotificationEntitiesCouchbaseTest(NotificationEntitiesCouchbaseTest.NotificationEntitiesCouchbaseFixture fixture)
    : NotificationEntitiesTestBase<NotificationEntitiesCouchbaseTest.NotificationEntitiesCouchbaseFixture>(fixture)
{
    public class NotificationEntitiesCouchbaseFixture : NotificationEntitiesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

public class OverzealousInitializationCouchbaseTest(OverzealousInitializationCouchbaseTest.OverzealousInitializationCouchbaseFixture fixture)
    : OverzealousInitializationTestBase<OverzealousInitializationCouchbaseTest.OverzealousInitializationCouchbaseFixture>(fixture)
{
    public class OverzealousInitializationCouchbaseFixture : OverzealousInitializationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Concurrency detector (concurrent use of one context) ----------
public class ConcurrencyDetectorEnabledCouchbaseTest(ConcurrencyDetectorEnabledCouchbaseTest.ConcurrencyDetectorCouchbaseFixture fixture)
    : ConcurrencyDetectorEnabledRelationalTestBase<ConcurrencyDetectorEnabledCouchbaseTest.ConcurrencyDetectorCouchbaseFixture>(fixture)
{
    public class ConcurrencyDetectorCouchbaseFixture : ConcurrencyDetectorFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

public class ConcurrencyDetectorDisabledCouchbaseTest(ConcurrencyDetectorDisabledCouchbaseTest.ConcurrencyDetectorCouchbaseFixture fixture)
    : ConcurrencyDetectorDisabledRelationalTestBase<ConcurrencyDetectorDisabledCouchbaseTest.ConcurrencyDetectorCouchbaseFixture>(fixture)
{
    public class ConcurrencyDetectorCouchbaseFixture : ConcurrencyDetectorFixtureBase
    {
        protected override string StoreName => "ConcurrencyDetectorDisabledTest";

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).EnableThreadSafetyChecks(enableChecks: false);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Materialization interceptors ----------
public class MaterializationInterceptionCouchbaseTest(NonSharedFixture fixture)
    : MaterializationInterceptionTestBase<MaterializationInterceptionCouchbaseTest.CouchbaseLibraryContext>(fixture),
        IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "MaterializationInterceptionTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public class CouchbaseLibraryContext(DbContextOptions options) : LibraryContext(options);
}

// ---------- Ad hoc save-path models ----------
public class NonSharedModelUpdatesCouchbaseTest(NonSharedFixture fixture)
    : NonSharedModelUpdatesTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "NonSharedModelUpdatesTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class NonSharedModelBulkUpdatesCouchbaseTest(NonSharedFixture fixture)
    : NonSharedModelBulkUpdatesRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "NonSharedModelBulkUpdatesTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

// ---------- HasData seeding through EnsureCreated ----------
public class SeedingCouchbaseTest : SeedingTestBase
{
    protected override TestStore TestStore => CouchbaseTestStore.Create("SeedingTest");

    protected override SeedingContext CreateContextWithEmptyDatabase(string testId) => new SeedingCouchbaseContext(testId);

    protected class SeedingCouchbaseContext(string testId) : SeedingContext(testId)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => new CouchbaseTestStore("SeedingTest", shared: true).AddProviderOptions(optionsBuilder);
    }
}

// ---------- Service registration and public API conventions ----------
public class CouchbaseServiceCollectionExtensionsTest() : RelationalServiceCollectionExtensionsTestBase(CouchbaseTestHelpers.Instance);

public class CouchbaseApiConsistencyTest(CouchbaseApiConsistencyTest.CouchbaseApiConsistencyFixture fixture)
    : ApiConsistencyTestBase<CouchbaseApiConsistencyTest.CouchbaseApiConsistencyFixture>(fixture)
{
    protected override void AddServices(ServiceCollection serviceCollection)
        => CouchbaseTestStoreFactory.Instance.AddProviderServices(serviceCollection);

    protected override Assembly TargetAssembly => typeof(CouchbaseDbContextOptionsExtensions).Assembly;

    public class CouchbaseApiConsistencyFixture : ApiConsistencyFixtureBase;
}

public class BadDataJsonDeserializationCouchbaseTest : BadDataJsonDeserializationTestBase;

// ---------- Interception of commands and query expression trees ----------
public abstract class CommandInterceptionCouchbaseTestBase(CommandInterceptionCouchbaseTestBase.InterceptionCouchbaseFixtureBase fixture)
    : CommandInterceptionTestBase(fixture)
{
    public abstract class InterceptionCouchbaseFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseTestStoreFactory.ForStore(StoreName);

        protected override string StoreName => "CommandInterception";

        protected override bool ShouldSubscribeToDiagnosticListener => false;

        protected override IServiceCollection InjectInterceptors(IServiceCollection serviceCollection, IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(CouchbaseTestStoreFactory.Instance.AddProviderServices(serviceCollection), injectedInterceptors);
    }
}

public class CommandInterceptionCouchbaseTest(CommandInterceptionCouchbaseTest.InterceptionCouchbaseFixture fixture)
    : CommandInterceptionCouchbaseTestBase(fixture), IClassFixture<CommandInterceptionCouchbaseTest.InterceptionCouchbaseFixture>
{
    public class InterceptionCouchbaseFixture : InterceptionCouchbaseFixtureBase;
}

public abstract class QueryExpressionInterceptionCouchbaseTestBase(QueryExpressionInterceptionCouchbaseTestBase.InterceptionCouchbaseFixtureBase fixture)
    : QueryExpressionInterceptionTestBase(fixture)
{
    public abstract class InterceptionCouchbaseFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseTestStoreFactory.ForStore(StoreName);

        protected override string StoreName => "QueryExpressionInterception";

        protected override bool ShouldSubscribeToDiagnosticListener => false;

        protected override IServiceCollection InjectInterceptors(IServiceCollection serviceCollection, IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(CouchbaseTestStoreFactory.Instance.AddProviderServices(serviceCollection), injectedInterceptors);
    }
}

public class QueryExpressionInterceptionCouchbaseTest(QueryExpressionInterceptionCouchbaseTest.InterceptionCouchbaseFixture fixture)
    : QueryExpressionInterceptionCouchbaseTestBase(fixture), IClassFixture<QueryExpressionInterceptionCouchbaseTest.InterceptionCouchbaseFixture>
{
    public class InterceptionCouchbaseFixture : InterceptionCouchbaseFixtureBase;
}
