using Couchbase.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseSavepointStatementsTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("rollback")]
    [InlineData("release")]
    public void GeneratedStatements_RoundTripThroughTheParser(string kind)
    {
        var delimited = "`my savepoint`";
        var (text, expected) = kind switch
        {
            "create" => (CouchbaseSavepointStatements.Create(delimited), SavepointOperation.Create),
            "rollback" => (CouchbaseSavepointStatements.RollbackTo(delimited), SavepointOperation.RollbackTo),
            _ => (CouchbaseSavepointStatements.Release(delimited), SavepointOperation.Release)
        };

        Assert.True(CouchbaseSavepointStatements.TryParse(text, out var parsedOperation, out var name));
        Assert.Equal(expected, parsedOperation);
        Assert.Equal("my savepoint", name);
    }

    [Fact]
    public void Parser_UnescapesDoubledBackticksInTheName()
    {
        Assert.True(CouchbaseSavepointStatements.TryParse("SAVEPOINT `a``b`", out _, out var name));
        Assert.Equal("a`b", name);
    }

    [Fact]
    public void Parser_IsCaseInsensitiveAndToleratesWhitespace()
    {
        Assert.True(CouchbaseSavepointStatements.TryParse("  rollback   to savepoint `sp` ; ", out var operation, out var name));
        Assert.Equal(SavepointOperation.RollbackTo, operation);
        Assert.Equal("sp", name);
    }

    [Theory]
    [InlineData("SELECT * FROM `default`.`blogs`.`blog`")]
    [InlineData("INSERT INTO `a` (KEY, VALUE) VALUES ('k', {})")]
    [InlineData("SAVEPOINT sp")]
    [InlineData("SAVEPOINT `sp` SELECT 1")]
    [InlineData("")]
    [InlineData(null)]
    public void Parser_IgnoresEverythingElse(string? text)
        => Assert.False(CouchbaseSavepointStatements.TryParse(text, out _, out _));
}
