// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindQueryFiltersQueryCouchbaseTest : NorthwindQueryFiltersQueryTestBase<
    NorthwindQueryCouchbaseFixture<NorthwindQueryFiltersCustomizer>>
{
    public NorthwindQueryFiltersQueryCouchbaseTest(
        NorthwindQueryCouchbaseFixture<NorthwindQueryFiltersCustomizer> fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        fixture.TestSqlLoggerFactory.Clear();
        fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Count_query(bool async)
    {
        await base.Count_query(async);

        AssertSql(
            """
@__ef_filter__TenantPrefix_0_startswith='B%' (Size = 2)

SELECT COUNT(*)
FROM "Customers" AS "c"
WHERE "c"."CompanyName" LIKE @__ef_filter__TenantPrefix_0_startswith ESCAPE '\'
""");
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
