// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindKeylessEntitiesQueryCouchbaseTest : NorthwindKeylessEntitiesQueryRelationalTestBase<
    NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindKeylessEntitiesQueryCouchbaseTest(
        NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override Task KeylessEntity_with_nav_defining_query(bool async)
        // FromSql mapping. Issue #21627.
        => Assert.ThrowsAsync<CouchbaseException>(() => base.KeylessEntity_with_nav_defining_query(async));
}
