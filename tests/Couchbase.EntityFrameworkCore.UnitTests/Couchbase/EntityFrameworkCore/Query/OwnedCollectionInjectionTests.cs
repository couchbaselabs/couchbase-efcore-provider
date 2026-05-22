using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

public class CouchbaseQueryEnumerableTests
{
    [Fact]
    public void TryGetPropertyCI_finds_camelCase_property_case_insensitively()
    {
        var json = JsonDocument.Parse(
            """{"contactMethods":[{"id":1,"type":"email","value":"a@b.com"}]}""");

        // Search with PascalCase (as EF Core navigation name)
        var found = json.RootElement.TryGetPropertyCI("ContactMethods", out var val);

        Assert.True(found);
        Assert.Equal(JsonValueKind.Array, val.ValueKind);
        Assert.Equal(1, val.GetArrayLength());
    }
}
