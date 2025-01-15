using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestStoreFactory : ITestStoreFactory
{
    public static CouchbaseTestStoreFactory Instance { get; } = new();
    
    //this needs to be fixed - AddEntityFrameworkCouchbase shouldn't register the Bucket or Cluster
   /* public IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
        => serviceCollection
            .AddEntityFrameworkCosmos()
            .AddSingleton<ILoggerFactory>(new TestSqlLoggerFactory())
            .AddSingleton<TestStoreIndex>();*/
   
    public virtual TestStore Create(string storeName) => 
        CouchbaseTestStore.Create(storeName);

    public virtual TestStore GetOrCreate(string storeName) => 
        CouchbaseTestStore.Create(storeName);
    
    
    public ListLoggerFactory CreateListLoggerFactory(Func<string, bool> shouldLogCategory) => 
        new TestSqlLoggerFactory(shouldLogCategory);
}