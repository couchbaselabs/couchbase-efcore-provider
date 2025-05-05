// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindAsNoTrackingQueryCouchbaseTest : CouchbaseNorthwindAsNoTrackingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindAsNoTrackingQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
        : base(fixture)
    {
    }
}
