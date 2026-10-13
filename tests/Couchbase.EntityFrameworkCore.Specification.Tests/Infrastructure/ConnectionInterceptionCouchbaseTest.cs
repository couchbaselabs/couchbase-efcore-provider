using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Infrastructure;

public abstract class ConnectionInterceptionCouchbaseTestBase(
    ConnectionInterceptionCouchbaseTestBase.InterceptionCouchbaseFixtureBase fixture)
    : ConnectionInterceptionTestBase(fixture)
{
    protected override DbContextOptionsBuilder ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
        => new CouchbaseTestStore("ConnectionInterception", shared: true).AddProviderOptions(optionsBuilder);

    protected override BadUniverseContext CreateBadUniverse(DbContextOptionsBuilder optionsBuilder)
        => new(optionsBuilder.UseCouchbase(
                new ClusterOptions().WithConnectionString("couchbase://unreachable.invalid"),
                o =>
                {
                    o.Bucket = CouchbaseTestEnvironment.Bucket;
                    o.Scope = "ConnectionInterception";
                })
            .Options);

    public abstract class InterceptionCouchbaseFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseTestStoreFactory.Instance;

        protected override string StoreName => "ConnectionInterception";

        protected override bool ShouldSubscribeToDiagnosticListener => false;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(
                CouchbaseTestStoreFactory.Instance.AddProviderServices(serviceCollection),
                injectedInterceptors);
    }

    public class ConnectionInterceptionCouchbaseTest(ConnectionInterceptionCouchbaseTest.InterceptionCouchbaseFixture fixture)
        : ConnectionInterceptionCouchbaseTestBase(fixture),
            IClassFixture<ConnectionInterceptionCouchbaseTest.InterceptionCouchbaseFixture>
    {
        public class InterceptionCouchbaseFixture : InterceptionCouchbaseFixtureBase;
    }
}
