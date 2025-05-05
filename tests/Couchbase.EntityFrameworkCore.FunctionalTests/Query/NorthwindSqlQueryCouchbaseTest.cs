// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using System.Data.Common;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public class NorthwindSqlQueryCouchbaseTest : NorthwindSqlQueryTestBase<NorthwindQueryCouchbaseFixture<NoopModelCustomizer>>
{
    public NorthwindSqlQueryCouchbaseTest(NorthwindQueryCouchbaseFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override DbParameter CreateDbParameter(string name, object value)
        => new CouchbaseParameter { ParameterName = name, Value = value };

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
