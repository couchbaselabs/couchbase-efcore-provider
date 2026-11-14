using System.Data.Common;
using System.Text.RegularExpressions;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

// Raw SQL++: FromSql/FromSqlRaw, Database.SqlQuery and ExecuteSql. EF's SQL is T-SQL; the
// relational test store rewrites it to SQL++ (see CouchbaseRawSql). The suites seed their own
// Northwind scopes through a RelationalTestStore, which they need for the raw-SQL helpers.

public class NorthwindRawSqlCouchbaseFixture : NorthwindQueryCouchbaseFixture<NoopModelCustomizer>
{
    // Own scope: ExecuteSql tests write, and the other Northwind suites must not see it.
    protected override string StoreName => "NorthwindRawSql";

    protected override ITestStoreFactory TestStoreFactory => CouchbaseRelationalTestStoreFactory.ForStore(StoreName);
}

public class FromSqlQueryCouchbaseTest(NorthwindRawSqlCouchbaseFixture fixture)
    : FromSqlQueryTestBase<NorthwindRawSqlCouchbaseFixture>(fixture)
{
    protected override DbParameter CreateDbParameter(string name, object value)
        => new CouchbaseParameter(CouchbaseRawSql.ParameterName(name)!, value);
}

public class SqlQueryCouchbaseTest(NorthwindRawSqlCouchbaseFixture fixture)
    : SqlQueryTestBase<NorthwindRawSqlCouchbaseFixture>(fixture)
{
    protected override DbParameter CreateDbParameter(string name, object value)
        => new CouchbaseParameter(CouchbaseRawSql.ParameterName(name)!, value);
}

public class NorthwindSqlQueryCouchbaseTest(NorthwindRawSqlCouchbaseFixture fixture)
    : NorthwindSqlQueryTestBase<NorthwindRawSqlCouchbaseFixture>(fixture)
{
    protected override DbParameter CreateDbParameter(string name, object value)
        => new CouchbaseParameter(CouchbaseRawSql.ParameterName(name)!, value);
}

public class NorthwindSqlExecutorCouchbaseFixture : NorthwindQueryCouchbaseFixture<SqlExecutorModelCustomizer>
{
    protected override string StoreName => "NorthwindSqlExecutor";

    protected override ITestStoreFactory TestStoreFactory => CouchbaseRelationalTestStoreFactory.ForStore(StoreName);

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).AddInterceptors(new QuotedIdentifierRewriter(StoreName));

    // SqlExecutorTestBase hard-codes ANSI-quoted SQL ("Customers") instead of going through the
    // test store's delimiter normalisation; in SQL++ a double-quoted name is a string literal. Rewrite
    // the command text the same way the store rewrites everyone else's raw SQL.
    private sealed class QuotedIdentifierRewriter(string scope) : DbCommandInterceptor
    {
        private static readonly Regex Quoted = new("\"(\\w+)\"");

        public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
        {
            if (result.CommandText.Contains('"'))
            {
                result.CommandText = CouchbaseRawSql.Normalize(
                    Quoted.Replace(result.CommandText, "[$1]"), CouchbaseTestEnvironment.Bucket, scope);
            }

            return result;
        }
    }
}

public class SqlExecutorCouchbaseTest(NorthwindSqlExecutorCouchbaseFixture fixture)
    : SqlExecutorTestBase<NorthwindSqlExecutorCouchbaseFixture>(fixture)
{
    // Couchbase has no stored procedures; the nearest equivalent is a SQL++ user-defined function.
    // No such functions are created, so the stored-procedure tests are expected to fail.
    protected override string TenMostExpensiveProductsSproc => "EXECUTE FUNCTION TenMostExpensiveProducts()";

    protected override string CustomerOrderHistorySproc => "EXECUTE FUNCTION CustomerOrderHistory($CustomerID)";

    protected override string CustomerOrderHistoryWithGeneratedParameterSproc => "EXECUTE FUNCTION CustomerOrderHistory({0})";

    protected override DbParameter CreateDbParameter(string name, object value)
        => new CouchbaseParameter(CouchbaseRawSql.ParameterName(name)!, value);
}
