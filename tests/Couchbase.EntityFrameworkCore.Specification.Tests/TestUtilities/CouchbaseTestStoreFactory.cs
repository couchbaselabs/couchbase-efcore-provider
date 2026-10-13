using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

public class CouchbaseTestStoreFactory : ITestStoreFactory
{
    public static CouchbaseTestStoreFactory Instance { get; } = new();

    protected CouchbaseTestStoreFactory()
    {
    }

    public virtual TestStore Create(string storeName) => CouchbaseTestStore.Create(storeName);

    public virtual TestStore GetOrCreate(string storeName) => CouchbaseTestStore.GetOrCreate(storeName);

    public virtual IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
    {
        // Unlike most providers, the Couchbase service registration is driven by an options
        // extension (it carries the cluster options and DateTimeFormat, and registers the
        // IBucketProvider the connection needs), so build a throwaway one to register from.
        var extension = (CouchbaseOptionsExtension)new CouchbaseTestStore("unused", shared: true)
            .AddProviderOptions(new DbContextOptionsBuilder())
            .Options.Extensions.Single(e => e is CouchbaseOptionsExtension);
        extension.ApplyServices(serviceCollection);
        return serviceCollection;
    }

    public virtual ListLoggerFactory CreateListLoggerFactory(Func<string, bool> shouldLogCategory)
        => new(shouldLogCategory);
}
