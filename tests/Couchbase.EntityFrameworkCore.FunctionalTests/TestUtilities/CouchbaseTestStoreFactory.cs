using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestStoreFactory : RelationalTestStoreFactory
{
    public static CouchbaseTestStoreFactory Instance { get; } = new();

    protected CouchbaseTestStoreFactory()
    {
    }

    public override TestStore Create(string storeName)
        => CouchbaseTestStore.Create(storeName);

    public override TestStore GetOrCreate(string storeName)
        => CouchbaseTestStore.GetOrCreate(storeName);

    public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter(level => level >= LogLevel.Debug);
                builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
            });

        var extension = new CouchbaseOptionsExtension(
            new CouchbaseDbContextOptionsBuilder(
                new DbContextOptionsBuilder(),
                new ClusterOptions()
                    .WithLogging(loggerFactory)
                    .WithConnectionString(TestEnvironment.ConnectionString)
                    .WithCredentials(TestEnvironment.Username, TestEnvironment.Password))
            {
                Scope = TestEnvironment.Scope,
                Bucket = TestEnvironment.BucketName
            });

        extension.ApplyServices(serviceCollection);
        return serviceCollection.AddEntityFrameworkCouchbase(extension);
    }
}
