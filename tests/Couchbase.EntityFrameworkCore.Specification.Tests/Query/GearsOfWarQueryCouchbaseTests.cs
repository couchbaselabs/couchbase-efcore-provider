using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// GearsOfWar: EF's largest query suite. A richer model than Northwind: TPH hierarchies (Gear/Officer,
// LocustLeader/LocustCommander, Faction/LocustHorde), composite keys, self-references, optional and
// required navigations, owned types, enums, DateTimeOffset/TimeSpan/byte[] columns.
public class GearsOfWarQueryCouchbaseFixture : GearsOfWarQueryRelationalFixture
{
    // The relational store: GearsOfWarFromSqlQueryTestBase needs its raw-SQL helpers.
    protected override ITestStoreFactory TestStoreFactory => CouchbaseRelationalTestStoreFactory.ForStore(StoreName);

    // GearsOfWar queries Include several collections; EF's test defaults make this warning an error.
    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context, generateIntKeys: true);
    }
}

public class GearsOfWarQueryCouchbaseTest(GearsOfWarQueryCouchbaseFixture fixture)
    : GearsOfWarQueryRelationalTestBase<GearsOfWarQueryCouchbaseFixture>(fixture);

public class GearsOfWarFromSqlQueryCouchbaseTest(GearsOfWarQueryCouchbaseFixture fixture)
    : GearsOfWarFromSqlQueryTestBase<GearsOfWarQueryCouchbaseFixture>(fixture);
