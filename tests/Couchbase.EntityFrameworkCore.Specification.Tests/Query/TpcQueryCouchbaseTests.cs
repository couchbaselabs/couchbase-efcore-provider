using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// TPC (table-per-concrete-type): each concrete type in its own collection, base-type queries become
// UNION ALL. The provider was never designed or tested for TPC; these suites measure how much works
// today, against the TPH versions of the same suites (TPHInheritanceQuery, GearsOfWarQuery).

public class TPCInheritanceQueryCouchbaseFixture : TPCInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance)
            // The harness gives TPC keys unique client-side values (CouchbaseSpecModel); EF warns that
            // a store-generated key may collide across TPC tables, which does not apply to them.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.TpcStoreGeneratedIdentityWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class TPCInheritanceQueryCouchbaseTest(TPCInheritanceQueryCouchbaseFixture fixture, ITestOutputHelper output)
    : TPCInheritanceQueryTestBase<TPCInheritanceQueryCouchbaseFixture>(fixture, output)
{
    // As for TPHInheritanceQuery: the cross-context transaction join is a no-op.
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction) { }
}

public class TPCGearsOfWarQueryCouchbaseFixture : TPCGearsOfWarQueryRelationalFixture
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

public class TPCGearsOfWarQueryCouchbaseTest(TPCGearsOfWarQueryCouchbaseFixture fixture)
    : TPCGearsOfWarQueryRelationalTestBase<TPCGearsOfWarQueryCouchbaseFixture>(fixture);
