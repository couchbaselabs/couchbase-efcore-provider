using System.Text.Json;
using Couchbase.Query;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

// Verifies the assumption the provider's query paths rely on: that QueryOptions.ScanConsistency(...)
// mutates the instance in place (the provider calls it as a bare statement, not using the return
// value) AND that the setting is actually applied to the request that gets POSTed. A same-instance
// return that silently dropped the value would still pass a reference check, so the request body is
// asserted too.
public class QueryOptionsBehaviorTests
{
    [Fact]
    public void ScanConsistency_MutatesInPlace_AndIsAppliedToTheRequest()
    {
        var options = QueryOptions.Create("SELECT 1"); // GetFormValuesAsJson requires a statement

        var returned = options.ScanConsistency(QueryScanConsistency.RequestPlus);
        Assert.Same(options, returned);

        // The value must be observable on the request the SDK builds, not just "not dropped".
        // Parse the request body and assert the scan_consistency field specifically, so a value
        // serialized under a different field/context wouldn't accidentally satisfy the test.
        using var doc = JsonDocument.Parse(options.GetFormValuesAsJson());
        Assert.True(doc.RootElement.TryGetProperty("scan_consistency", out var scanConsistency),
            "request body has no scan_consistency field");
        Assert.Equal("request_plus", scanConsistency.GetString());
    }
}
