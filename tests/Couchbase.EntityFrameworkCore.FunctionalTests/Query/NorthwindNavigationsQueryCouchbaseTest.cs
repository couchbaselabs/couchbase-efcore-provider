// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindNavigationsQueryCouchbaseTest : NorthwindNavigationsQueryRelationalTestBase<
    NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindNavigationsQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
        : base(fixture)
    {
    }
}
