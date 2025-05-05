// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

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
