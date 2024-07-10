using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

[CollectionDefinition(Name)]
public class CouchbaseTestingCollection : ICollectionFixture<CouchbaseFixture>
{
    public const string Name = "CouchbaseTestingCollection";

    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}