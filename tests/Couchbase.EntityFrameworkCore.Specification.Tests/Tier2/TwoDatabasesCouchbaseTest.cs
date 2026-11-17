using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Tier2;

// Two contexts over two databases, and setting the connection string after configuration. A
// relational "database" maps to a scope here: buckets must already exist on the cluster.
public class TwoDatabasesCouchbaseTest(TwoDatabasesCouchbaseTest.TwoDatabasesCouchbaseFixture fixture)
    : TwoDatabasesTestBase(fixture), IClassFixture<TwoDatabasesCouchbaseTest.TwoDatabasesCouchbaseFixture>
{
    protected override DbContextOptionsBuilder CreateTestOptions(
        DbContextOptionsBuilder optionsBuilder, bool withConnectionString = false, bool withNullConnectionString = false)
        => withConnectionString
            ? optionsBuilder.UseCouchbase(withNullConnectionString ? null! : DummyConnectionString, o => o.Bucket = CouchbaseTestEnvironment.Bucket)
            : CouchbaseTestHelpers.Instance.UseProviderOptions(optionsBuilder);

    protected override TwoDatabasesWithDataContext CreateBackingContext(string databaseName)
        => new(new CouchbaseTestStore(databaseName, shared: false).AddProviderOptions(new DbContextOptionsBuilder()).Options);

    protected override string DummyConnectionString => "couchbase://does-not-exist.invalid";

    public class TwoDatabasesCouchbaseFixture : FixtureBase;
}
