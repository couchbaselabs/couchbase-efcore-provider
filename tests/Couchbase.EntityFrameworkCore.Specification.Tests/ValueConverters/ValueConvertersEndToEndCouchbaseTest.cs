using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.ValueConverters;

public class ValueConvertersEndToEndCouchbaseTest(
    ValueConvertersEndToEndCouchbaseTest.ValueConvertersEndToEndCouchbaseFixture fixture)
    : ValueConvertersEndToEndTestBase<ValueConvertersEndToEndCouchbaseTest.ValueConvertersEndToEndCouchbaseFixture>(fixture)
{
    public class ValueConvertersEndToEndCouchbaseFixture : ValueConvertersEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseTestStoreFactory.ForStore(StoreName);

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            // The provider qualifies bare table names into Bucket.Scope.Collection keyspaces here,
            // from the context's configured bucket and scope (the store name).
            modelBuilder.ConfigureToCouchbase(context);
        }
    }
}
