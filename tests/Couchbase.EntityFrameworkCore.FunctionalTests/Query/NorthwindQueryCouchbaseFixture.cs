using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestSqlLoggerFactory = Microsoft.EntityFrameworkCore.TestUtilities.TestSqlLoggerFactory;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Query;
public class NorthwindQueryCouchbaseFixture<TModelCustomizer> :CouchbaseNorthwindQueryFixtureBase<TModelCustomizer>
    where TModelCustomizer : IModelCustomizer, new()
{

    public static IEnumerable<object[]> IsAsyncData = new[] { new object[] { true } };

    public NorthwindQueryCouchbaseFixture()
    {
    }
    protected override ITestStoreFactory TestStoreFactory
        => CouchbaseNorthwindTestStoreFactory.Instance;

    protected override bool UsePooling
        => false;

    public TestSqlLoggerFactory TestSqlLoggerFactory
        => (TestSqlLoggerFactory)ServiceProvider.GetRequiredService<ILoggerFactory>();

    protected override bool ShouldLogCategory(string logCategory)
        => logCategory == DbLoggerCategory.Query.Name;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        modelBuilder
            .Entity<CustomerQuery>()
            .HasDiscriminator<string>("Discriminator").HasValue("Customer");

        modelBuilder
            .Entity<OrderQuery>()
            .HasDiscriminator<string>("Discriminator").HasValue("Order");

        modelBuilder
            .Entity<ProductQuery>()
            .HasDiscriminator<string>("Discriminator").HasValue("Product");

        modelBuilder
            .Entity<CustomerQueryWithQueryFilter>()
            .HasDiscriminator<string>("Discriminator").HasValue("Customer");

        modelBuilder.ConfigureToCouchbase(context);
    }

    protected override void Seed(NorthwindContext context)
        => base.Seed(context);
}
