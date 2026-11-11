using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

public class NorthwindFunctionsQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindFunctionsQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);
