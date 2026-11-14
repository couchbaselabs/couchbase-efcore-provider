using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

public class NorthwindWhereQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindWhereQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);
