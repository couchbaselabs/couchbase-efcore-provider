using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

[Collection(CouchbaseTestingCollection.Name)]
public class FromRawSqlTests
{
    private readonly CouchbaseFixture _couchbaseFixture;
    private readonly ITestOutputHelper _outputHelper;

    public FromRawSqlTests(CouchbaseFixture couchbaseFixture, ITestOutputHelper outputHelper)
    {
        _couchbaseFixture = couchbaseFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_META()
    {
        using (var context = new BloggingContext())
        {
            await _couchbaseFixture.InitializeBloggingAsync();
            var statement = "SELECT `b`.* FROM `Content`.`Blogs`.`Blog` as `b` WHERE META().id = \"2\"";
            var blog = await context.Blogs.FromSqlRaw(statement).AsNoTracking().FirstOrDefaultAsync();
            
            Assert.NotNull(blog);
        }
    }
}