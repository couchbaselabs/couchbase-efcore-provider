using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestStoreTests
{
    [Fact]
    public void Test_Initialization()
    {
        var store = CouchbaseTestStore.Create("northwind");
    }
}
