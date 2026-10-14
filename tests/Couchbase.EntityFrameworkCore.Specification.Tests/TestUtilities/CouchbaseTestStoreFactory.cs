using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

public class CouchbaseTestStoreFactory : ITestStoreFactory
{
    // The scope the registered services are built for. Couchbase takes its bucket and scope from the
    // options extension the services are registered from (see AddProviderServices), not from each
    // context's own options, so a fixture that stores data must use ForStore(StoreName); otherwise
    // every fixture's data ends up in this one shared scope.
    private readonly string _scope;

    /// <summary>For fixtures that never read or write data (they only need the services).</summary>
    public static CouchbaseTestStoreFactory Instance { get; } = new("unused");

    /// <summary>A factory whose services use the store name as the Couchbase scope.</summary>
    public static CouchbaseTestStoreFactory ForStore(string storeName) => new(storeName);

    protected CouchbaseTestStoreFactory(string scope)
    {
        _scope = scope;
    }

    public virtual TestStore Create(string storeName) => CouchbaseTestStore.Create(storeName);

    public virtual TestStore GetOrCreate(string storeName) => CouchbaseTestStore.GetOrCreate(storeName);

    public virtual IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
    {
        // Unlike most providers, the Couchbase service registration is driven by an options
        // extension (it carries the cluster options and DateTimeFormat, and registers the
        // IBucketProvider the connection needs), so build a throwaway one to register from.
        var extension = (CouchbaseOptionsExtension)new CouchbaseTestStore(_scope, shared: true)
            .AddProviderOptions(new DbContextOptionsBuilder())
            .Options.Extensions.Single(e => e is CouchbaseOptionsExtension);
        extension.ApplyServices(serviceCollection);
        return serviceCollection;
    }

    public virtual ListLoggerFactory CreateListLoggerFactory(Func<string, bool> shouldLogCategory)
        => new(shouldLogCategory);
}
