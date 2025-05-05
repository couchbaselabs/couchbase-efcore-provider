// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

/*using Couchbase.EntityFrameworkCore.Internal;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindEFPropertyIncludeQuerySqliteTest : NorthwindEFPropertyIncludeQueryTestBase<
    NorthwindQuerySqliteFixture<NoopModelCustomizer>>
{
    public NorthwindEFPropertyIncludeQuerySqliteTest(
        NorthwindQuerySqliteFixture<NoopModelCustomizer> fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        //TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
    }

    public override async Task Filtered_include_with_multiple_ordering(bool async)
        => Assert.Equal(
            SqliteStrings.ApplyNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Filtered_include_with_multiple_ordering(async))).Message);

    public override async Task Include_collection_with_cross_apply_with_filter(bool async)
        => Assert.Equal(
            SqliteStrings.ApplyNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Include_collection_with_cross_apply_with_filter(async))).Message);

    public override async Task Include_collection_with_outer_apply_with_filter(bool async)
        => Assert.Equal(
            SqliteStrings.ApplyNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Include_collection_with_outer_apply_with_filter(async))).Message);

    public override async Task Include_collection_with_outer_apply_with_filter_non_equality(bool async)
        => Assert.Equal(
            SqliteStrings.ApplyNotSupported,
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Include_collection_with_outer_apply_with_filter_non_equality(async))).Message);

    public override async Task Include_collection_with_last_no_orderby(bool async)
        => Assert.Equal(
            RelationalStrings.LastUsedWithoutOrderBy(nameof(Enumerable.Last)),
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => base.Include_collection_with_last_no_orderby(async))).Message);
}*/
