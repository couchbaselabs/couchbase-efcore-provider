using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseNorthwindTestStoreFactory : CouchbaseTestStoreFactory
{
    private const string Name = "Northwind";
    
    protected CouchbaseNorthwindTestStoreFactory()
    {
    }
    
    public new static CouchbaseNorthwindTestStoreFactory Instance { get; } = new();
    
    public override TestStore GetOrCreate(string storeName)
        => CouchbaseTestStore.GetOrCreate(Name, "Northwind.json");

}