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

        private const string UseTransactionSkip =
            "The test calls DbConnection.BeginTransaction() on the still-closed connection; CouchbaseConnection requires it to be open first, and EF doesn't open it here.";

        [Theory(Skip = UseTransactionSkip), InlineData(false), InlineData(true)]
        public override Task UseTransaction_without_interceptor(bool async) => base.UseTransaction_without_interceptor(async);

        [Theory(Skip = UseTransactionSkip), InlineData(false), InlineData(true)]
        public override Task Intercept_UseTransaction_to_wrap(bool async) => base.Intercept_UseTransaction_to_wrap(async);

        [Theory(Skip = UseTransactionSkip), InlineData(false), InlineData(true)]
        public override Task Intercept_UseTransaction(bool async) => base.Intercept_UseTransaction(async);

    }
}