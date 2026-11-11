using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Associations;

// Owned entity types embedded in the document. These suites run EF's shared "associations" model
// with owned types; the ComplexJson suites run the same model with complex types, so the two are
// directly comparable.

// ---------- Associations matrix, owned types mapped to JSON (OwnsOne/OwnsMany + ToJson) ----------
public class OwnedJsonCouchbaseFixture : OwnedJsonRelationalFixtureBase
{
    // Queries Include several collections; EF's test defaults turn this performance warning into an
    // error (as in GraphUpdatesCouchbaseTest).
    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    // As for ComplexJson: real Couchbase transactions; only the cross-context join is a no-op.
    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class OwnedJsonProjectionCouchbaseTest(OwnedJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedJsonProjectionRelationalTestBase<OwnedJsonCouchbaseFixture>(fixture, output);

public class OwnedJsonCollectionCouchbaseTest(OwnedJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedJsonCollectionRelationalTestBase<OwnedJsonCouchbaseFixture>(fixture, output);

public class OwnedJsonMiscellaneousCouchbaseTest(OwnedJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedJsonMiscellaneousRelationalTestBase<OwnedJsonCouchbaseFixture>(fixture, output);

public class OwnedJsonPrimitiveCollectionCouchbaseTest(OwnedJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedJsonPrimitiveCollectionRelationalTestBase<OwnedJsonCouchbaseFixture>(fixture, output);

public class OwnedJsonStructuralEqualityCouchbaseTest(OwnedJsonCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedJsonStructuralEqualityRelationalTestBase<OwnedJsonCouchbaseFixture>(fixture, output);

// Own store, as for ComplexJsonBulkUpdate: bulk writes inside a transaction are not rolled back.
public class OwnedJsonBulkUpdateCouchbaseFixture : OwnedJsonCouchbaseFixture
{
    protected override string StoreName => "OwnedJsonBulkUpdate";
}

public class OwnedJsonBulkUpdateCouchbaseTest(OwnedJsonBulkUpdateCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedJsonBulkUpdateRelationalTestBase<OwnedJsonBulkUpdateCouchbaseFixture>(fixture, output);

// ---------- Associations matrix, owned navigations (relational: separate tables; here embedded) ----------
public class OwnedNavigationsCouchbaseFixture : OwnedNavigationsRelationalFixtureBase
{
    // Queries Include several collections; EF's test defaults turn this performance warning into an
    // error (as in GraphUpdatesCouchbaseTest).
    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class OwnedNavigationsProjectionCouchbaseTest(OwnedNavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedNavigationsProjectionRelationalTestBase<OwnedNavigationsCouchbaseFixture>(fixture, output);

public class OwnedNavigationsCollectionCouchbaseTest(OwnedNavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedNavigationsCollectionRelationalTestBase<OwnedNavigationsCouchbaseFixture>(fixture, output);

public class OwnedNavigationsMiscellaneousCouchbaseTest(OwnedNavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedNavigationsMiscellaneousRelationalTestBase<OwnedNavigationsCouchbaseFixture>(fixture, output);

public class OwnedNavigationsPrimitiveCollectionCouchbaseTest(OwnedNavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedNavigationsPrimitiveCollectionRelationalTestBase<OwnedNavigationsCouchbaseFixture>(fixture, output);

public class OwnedNavigationsSetOperationsCouchbaseTest(OwnedNavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedNavigationsSetOperationsRelationalTestBase<OwnedNavigationsCouchbaseFixture>(fixture, output);

public class OwnedNavigationsStructuralEqualityCouchbaseTest(OwnedNavigationsCouchbaseFixture fixture, ITestOutputHelper output)
    : OwnedNavigationsStructuralEqualityRelationalTestBase<OwnedNavigationsCouchbaseFixture>(fixture, output);

// ---------- JSON-mapped owned entities: query (EF's main JSON suite) ----------
public class JsonQueryCouchbaseFixture : JsonQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        modelBuilder.IgnoreNestedPrimitiveCollections();
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class JsonQueryCouchbaseTest(JsonQueryCouchbaseFixture fixture)
    : JsonQueryRelationalTestBase<JsonQueryCouchbaseFixture>(fixture);

// ---------- JSON-mapped owned entities: updates ----------
// The suite joins each context to a shared transaction (DatabaseFacade.UseTransaction) and rolls
// it back. The relational test store shares one CouchbaseConnection between contexts, which is
// what makes that join work, as in TransactionCouchbaseTest.
public class JsonUpdateCouchbaseTest(JsonUpdateCouchbaseTest.JsonUpdateCouchbaseFixture fixture)
    : JsonUpdateTestBase<JsonUpdateCouchbaseTest.JsonUpdateCouchbaseFixture>(fixture)
{
    protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();

    public class JsonUpdateCouchbaseFixture : JsonUpdateFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseRelationalTestStoreFactory.ForStore(StoreName);

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            modelBuilder.IgnoreNestedPrimitiveCollections();
            CouchbaseSpecModel.Apply(modelBuilder, context);
        }
    }
}

// ---------- Owned entities (EF's owned-type query suite) ----------
public class OwnedQueryCouchbaseTest(OwnedQueryCouchbaseTest.OwnedQueryCouchbaseFixture fixture)
    : OwnedQueryRelationalTestBase<OwnedQueryCouchbaseTest.OwnedQueryCouchbaseFixture>(fixture)
{
    public class OwnedQueryCouchbaseFixture : RelationalOwnedQueryFixture
    {
        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

        protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
        }
    }
}

// ---------- Ad hoc models (a fresh model per test, one scope per suite) ----------
public class JsonTypesCouchbaseTest(NonSharedFixture fixture)
    : JsonTypesRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "JsonTypesTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class OwnedEntityQueryCouchbaseTest(NonSharedFixture fixture)
    : OwnedEntityQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "OwnedEntityQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class AdHocJsonQueryCouchbaseTest(NonSharedFixture fixture)
    : AdHocJsonQueryRelationalTestBase(fixture), IClassFixture<NonSharedFixture>
{
    protected override string StoreName => "AdHocJsonQueryTest";

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    // These seeds insert hand-written (often deliberately malformed) JSON with raw SQL on other
    // providers. Not ported yet: the tests that use them fail with this message.
    private static Task NotPorted() => throw new NotImplementedException("Raw JSON seed not ported to Couchbase (harness gap).");

    protected override Task Seed30028(DbContext ctx) => NotPorted();

    protected override Task Seed33046(DbContext ctx) => NotPorted();

    protected override Task SeedJunkInJson(DbContext ctx) => NotPorted();

    protected override Task SeedTrickyBuffering(DbContext ctx) => NotPorted();

    protected override Task SeedShadowProperties(DbContext ctx) => NotPorted();

    protected override Task SeedNotICollection(DbContext ctx) => NotPorted();

    protected override Task SeedBadJsonProperties(ContextBadJsonProperties ctx) => NotPorted();
}
