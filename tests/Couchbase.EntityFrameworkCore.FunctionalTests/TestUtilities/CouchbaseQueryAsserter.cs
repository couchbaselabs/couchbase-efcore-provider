using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseQueryAsserter : QueryAsserter
{
    public CouchbaseQueryAsserter(IQueryFixtureBase queryFixture, Func<Expression, Expression> rewriteExpectedQueryExpression, Func<Expression, Expression> rewriteServerQueryExpression, bool ignoreEntryCount = false)
        : base(queryFixture, rewriteExpectedQueryExpression, rewriteServerQueryExpression, ignoreEntryCount)
    {
    }
}
