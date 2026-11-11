using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

public class NorthwindGroupByQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindGroupByQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);
