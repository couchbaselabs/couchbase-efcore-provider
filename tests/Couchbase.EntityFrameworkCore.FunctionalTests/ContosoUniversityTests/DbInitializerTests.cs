using ContosoUniversity.Data;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;

[Collection(ContosoTestingCollection.Name)]
public class DbInitializerTests
{
    private readonly ContosoFixture _contosoFixture;
    private readonly ITestOutputHelper _outputHelper;

    public DbInitializerTests(ContosoFixture contosoFixture, ITestOutputHelper outputHelper)
    {
        _contosoFixture = contosoFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public void Test_Initialize()
    {
        DbInitializer.Initialize(_contosoFixture.DbContext);
    }
}