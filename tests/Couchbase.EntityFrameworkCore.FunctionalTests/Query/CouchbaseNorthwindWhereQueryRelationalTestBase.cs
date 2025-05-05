// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;


public abstract class CouchbaseNorthwindWhereQueryRelationalTestBase<TFixture> : CouchbaseNorthwindWhereQueryTestBase<TFixture>
    where TFixture : NorthwindQueryCouchbaseFixture<NoopModelCustomizer>, new()
{
    protected CouchbaseNorthwindWhereQueryRelationalTestBase(TFixture fixture)
        : base(fixture)
    {
    }

    public override Task Where_bool_client_side_negated(bool async)
        => AssertTranslationFailed(() => base.Where_bool_client_side_negated(async));

    public override Task Where_equals_method_string_with_ignore_case(bool async)
        => AssertTranslationFailed(() => base.Where_equals_method_string_with_ignore_case(async));

    protected virtual bool CanExecuteQueryString
        => false;

    protected override QueryAsserter CreateQueryAsserter(TFixture fixture)
        => new RelationalQueryAsserter(
            fixture, RewriteExpectedQueryExpression, RewriteServerQueryExpression, canExecuteQueryString: CanExecuteQueryString);
}

