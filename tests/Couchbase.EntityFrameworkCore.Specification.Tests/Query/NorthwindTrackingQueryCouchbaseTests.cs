using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// Group B: further Northwind suites on the existing Northwind fixture and data.

public class NorthwindAsNoTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindAsNoTrackingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindAsTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindAsTrackingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindChangeTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindChangeTrackingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindSetOperationsQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindSetOperationsQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindQueryFiltersQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NorthwindQueryFiltersCustomizer> fixture)
    : NorthwindQueryFiltersQueryTestBase<NorthwindQueryCouchbaseFixture<NorthwindQueryFiltersCustomizer>>(fixture);

public class NorthwindKeylessEntitiesQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindKeylessEntitiesQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindCompiledQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindCompiledQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);

public class NorthwindDbFunctionsQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindDbFunctionsQueryRelationalTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture)
{
    // SQL++ has no named collations; the collation tests are expected to fail.
    protected override string CaseInsensitiveCollation => "unsupported_ci";

    protected override string CaseSensitiveCollation => "unsupported_cs";
}

public class NorthwindIncludeNoTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
    : NorthwindIncludeNoTrackingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>(fixture);
