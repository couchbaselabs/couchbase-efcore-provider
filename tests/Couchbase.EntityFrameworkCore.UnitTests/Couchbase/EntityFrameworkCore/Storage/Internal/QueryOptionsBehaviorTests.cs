using Couchbase.Query;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

// Verifies the assumption the provider's query paths rely on: that QueryOptions.ScanConsistency(...)
// mutates the instance in place (so calling it as a bare statement, without using the return value,
// actually applies). If it returns a new instance instead, the provider silently loses the setting.
public class QueryOptionsBehaviorTests
{
    [Fact]
    public void ScanConsistency_MutatesInPlace_AndReturnsSameInstance()
    {
        var options = new QueryOptions();
        var returned = options.ScanConsistency(QueryScanConsistency.RequestPlus);
        Assert.Same(options, returned);
    }
}
