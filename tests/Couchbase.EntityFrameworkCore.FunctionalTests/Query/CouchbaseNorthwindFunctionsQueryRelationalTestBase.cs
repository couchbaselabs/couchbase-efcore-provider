// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;

public abstract class CouchbaseNorthwindFunctionsQueryRelationalTestBase<TFixture> : CouchbaseNorthwindFunctionsQueryTestBase<TFixture>
    where TFixture : NorthwindQueryFixtureBase<NoopModelCustomizer>, new()
{
    protected CouchbaseNorthwindFunctionsQueryRelationalTestBase(TFixture fixture)
        : base(fixture)
    {
    }

    protected virtual bool CanExecuteQueryString
        => false;

    protected override QueryAsserter CreateQueryAsserter(TFixture fixture)
        => new RelationalQueryAsserter(
            fixture, RewriteExpectedQueryExpression, RewriteServerQueryExpression, canExecuteQueryString: CanExecuteQueryString);
}


