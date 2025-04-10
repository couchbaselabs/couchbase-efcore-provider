// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Couchbase.EntityFrameworkCore.FunctionalTests.TestModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindChangeTrackingQueryCouchbaseTest : CouchbaseNorthwindChangeTrackingQueryTestBase<
    NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindChangeTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
        : base(fixture)
    {
    }

    protected override NorthwindContext CreateNoTrackingContext()
        => new NorthwindCouchbaseContext(
            new DbContextOptionsBuilder(Fixture.CreateOptions())
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
}
