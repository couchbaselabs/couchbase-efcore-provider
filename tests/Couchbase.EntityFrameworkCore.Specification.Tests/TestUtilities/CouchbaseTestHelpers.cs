using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// Lets EF's spec fixtures build models and services the way the Couchbase provider does
/// (e.g. <c>F1FixtureBase.CreateModelBuilder</c> uses it to get the provider's convention set).
/// </summary>
public class CouchbaseTestHelpers : RelationalTestHelpers
{
    protected CouchbaseTestHelpers()
    {
    }

    public static CouchbaseTestHelpers Instance { get; } = new();

    public override IServiceCollection AddProviderServices(IServiceCollection services)
        => CouchbaseTestStoreFactory.Instance.AddProviderServices(services);

    public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
        => new CouchbaseTestStore("TestHelpers", shared: true).AddProviderOptions(optionsBuilder);
}
