using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Infrastructure;

public abstract class TransactionInterceptionCouchbaseTestBase(
    TransactionInterceptionCouchbaseTestBase.InterceptionCouchbaseFixtureBase fixture)
    : TransactionInterceptionTestBase(fixture)
{
    public abstract class InterceptionCouchbaseFixtureBase : InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseTestStoreFactory.Instance;

        protected override string StoreName => "TransactionInterception";

        protected override bool ShouldSubscribeToDiagnosticListener => false;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(
                CouchbaseTestStoreFactory.Instance.AddProviderServices(serviceCollection),
                injectedInterceptors);
    }

    public class TransactionInterceptionCouchbaseTest(TransactionInterceptionCouchbaseTest.InterceptionCouchbaseFixture fixture)
        : TransactionInterceptionCouchbaseTestBase(fixture),
            IClassFixture<TransactionInterceptionCouchbaseTest.InterceptionCouchbaseFixture>
    {
        public class InterceptionCouchbaseFixture : InterceptionCouchbaseFixtureBase;

        // The UseTransaction tests call context.Database.GetDbConnection().BeginTransaction()
        // directly. The upstream providers hand EF a DbConnection that the test store has already
        // opened, so that works there; Couchbase's connection is created by EF and starts closed,
        // and, like SqlConnection or NpgsqlConnection, refuses to begin a transaction until it
        // is open. Open it as the tests' precondition, but only for these tests: the others must
        // keep seeing EF open and close the connection itself.
        private bool _openConnectionWhenSeeding;

        public override async Task<UniverseContext> SeedAsync(UniverseContext context)
        {
            if (_openConnectionWhenSeeding)
            {
                await context.Database.OpenConnectionAsync();
            }

            return await base.SeedAsync(context);
        }

        [Theory, InlineData(false), InlineData(true)]
        public override Task UseTransaction_without_interceptor(bool async)
        {
            _openConnectionWhenSeeding = true;
            return base.UseTransaction_without_interceptor(async);
        }

        [Theory, InlineData(false), InlineData(true)]
        public override Task Intercept_UseTransaction_to_wrap(bool async)
        {
            _openConnectionWhenSeeding = true;
            return base.Intercept_UseTransaction_to_wrap(async);
        }

        [Theory, InlineData(false), InlineData(true)]
        public override Task Intercept_UseTransaction(bool async)
        {
            _openConnectionWhenSeeding = true;
            return base.Intercept_UseTransaction(async);
        }

    }
}