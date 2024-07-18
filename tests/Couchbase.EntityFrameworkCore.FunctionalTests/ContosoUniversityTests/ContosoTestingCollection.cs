using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;

[CollectionDefinition(Name)]
public class ContosoTestingCollection: ICollectionFixture<ContosoFixture>
{
    public const string Name = "ContosoTestingCollection";

    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}