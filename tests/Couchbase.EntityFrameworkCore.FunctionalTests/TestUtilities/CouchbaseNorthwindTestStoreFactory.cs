using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseNorthwindTestStoreFactory : CouchbaseTestStoreFactory
{
    public static new CouchbaseNorthwindTestStoreFactory Instance { get; } = new();

    protected CouchbaseNorthwindTestStoreFactory()
    {
    }

    public override TestStore GetOrCreate(string storeName)
        => CouchbaseTestStore.GetOrCreate("northwind");
}
