// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindCompiledQueryCouchbaseTest : NorthwindCompiledQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindCompiledQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture)
        : base(fixture)
    {
    }
}
