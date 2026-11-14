using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

public class NorthwindSelectQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindSelectQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);
