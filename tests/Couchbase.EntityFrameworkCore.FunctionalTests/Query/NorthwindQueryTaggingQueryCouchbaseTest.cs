// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindQueryTaggingQueryCouchbaseTest : NorthwindQueryTaggingQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindQueryTaggingQueryCouchbaseTest(
        NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
    }
}
