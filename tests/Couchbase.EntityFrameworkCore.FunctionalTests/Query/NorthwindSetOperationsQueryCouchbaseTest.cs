// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindSetOperationsQueryCouchbaseTest : NorthwindSetOperationsQueryRelationalTestBase<
    NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindSetOperationsQueryCouchbaseTest(
        NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Client_eval_Union_FirstOrDefault(bool async)
        // Client evaluation in projection. Issue #16243.
        => Assert.Equal(
            RelationalStrings.SetOperationsNotAllowedAfterClientEvaluation,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Client_eval_Union_FirstOrDefault(async))).Message);
}
