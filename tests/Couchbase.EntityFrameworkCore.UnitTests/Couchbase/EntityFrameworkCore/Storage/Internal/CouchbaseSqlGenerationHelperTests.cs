using System.Text;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseSqlGenerationHelperTests
{
    private readonly CouchbaseSqlGenerationHelper _helper;

    public CouchbaseSqlGenerationHelperTests()
    {
        var dependencies = new RelationalSqlGenerationHelperDependencies();
        _helper = new CouchbaseSqlGenerationHelper(dependencies);
    }

    [Fact]
    public void DelimitIdentifier_WrapsInBackticks()
    {
        // Act
        var result = _helper.DelimitIdentifier("myIdentifier");

        // Assert
        Assert.Equal("`myIdentifier`", result);
    }

    [Fact]
    public void DelimitIdentifier_EscapesBackticks()
    {
        // Act
        var result = _helper.DelimitIdentifier("my`identifier");

        // Assert
        Assert.Equal("`my``identifier`", result);
    }

    [Fact]
    public void DelimitIdentifier_EscapesMultipleBackticks()
    {
        // Act
        var result = _helper.DelimitIdentifier("a`b`c");

        // Assert
        Assert.Equal("`a``b``c`", result);
    }

    [Fact]
    public void DelimitIdentifier_EscapesConsecutiveBackticks()
    {
        // Act
        var result = _helper.DelimitIdentifier("test``name");

        // Assert
        Assert.Equal("`test````name`", result);
    }

    [Fact]
    public void DelimitIdentifier_EscapesBacktickAtStart()
    {
        // Act
        var result = _helper.DelimitIdentifier("`start");

        // Assert
        Assert.Equal("```start`", result);
    }

    [Fact]
    public void DelimitIdentifier_EscapesBacktickAtEnd()
    {
        // Act
        var result = _helper.DelimitIdentifier("end`");

        // Assert
        Assert.Equal("`end```", result);
    }

    [Fact]
    public void DelimitIdentifier_StringBuilder_WrapsInBackticks()
    {
        // Arrange
        var builder = new StringBuilder();

        // Act
        _helper.DelimitIdentifier(builder, "myIdentifier");

        // Assert
        Assert.Equal("`myIdentifier`", builder.ToString());
    }

    [Fact]
    public void DelimitIdentifier_StringBuilder_EscapesBackticks()
    {
        // Arrange
        var builder = new StringBuilder();

        // Act
        _helper.DelimitIdentifier(builder, "my`identifier");

        // Assert
        Assert.Equal("`my``identifier`", builder.ToString());
    }

    [Fact]
    public void EscapeIdentifier_DoublesBackticks()
    {
        // Act
        var result = _helper.EscapeIdentifier("my`identifier");

        // Assert
        Assert.Equal("my``identifier", result);
    }

    [Fact]
    public void EscapeIdentifier_NoBackticks_ReturnsUnchanged()
    {
        // Act
        var result = _helper.EscapeIdentifier("myIdentifier");

        // Assert
        Assert.Equal("myIdentifier", result);
    }

    [Fact]
    public void EscapeIdentifier_StringBuilder_DoublesBackticks()
    {
        // Arrange
        var builder = new StringBuilder();

        // Act
        _helper.EscapeIdentifier(builder, "my`identifier");

        // Assert
        Assert.Equal("my``identifier", builder.ToString());
    }

    [Fact]
    public void EscapeIdentifier_StringBuilder_NoBackticks_AppendsUnchanged()
    {
        // Arrange
        var builder = new StringBuilder();

        // Act
        _helper.EscapeIdentifier(builder, "myIdentifier");

        // Assert
        Assert.Equal("myIdentifier", builder.ToString());
    }

    [Fact]
    public void GenerateParameterName_AddsPrefix()
    {
        // Act
        var result = _helper.GenerateParameterName("param");

        // Assert
        Assert.Equal("$param", result);
    }

    [Fact]
    public void GenerateParameterName_AlreadyPrefixed_ReturnsUnchanged()
    {
        // Act
        var result = _helper.GenerateParameterName("$param");

        // Assert
        Assert.Equal("$param", result);
    }

    [Fact]
    public void GenerateParameterName_StringBuilder_AddsPrefix()
    {
        // Arrange
        var builder = new StringBuilder();

        // Act
        _helper.GenerateParameterName(builder, "param");

        // Assert
        Assert.Equal("$param", builder.ToString());
    }

    // -----------------------------------------------------------------------
    // Dotted (multi-part) identifier splitting — Couchbase keyspace names
    // -----------------------------------------------------------------------

    [Fact]
    public void DelimitIdentifier_TwoPart_SplitsEachSegment()
    {
        var result = _helper.DelimitIdentifier("bucket.scope");

        Assert.Equal("`bucket`.`scope`", result);
    }

    [Fact]
    public void DelimitIdentifier_ThreePart_SplitsAllThreeSegments()
    {
        var result = _helper.DelimitIdentifier("default.blogs.personphoto");

        Assert.Equal("`default`.`blogs`.`personphoto`", result);
    }

    [Fact]
    public void DelimitIdentifier_ThreePart_WithCamelCaseSegments()
    {
        var result = _helper.DelimitIdentifier("myBucket.myScope.myCollection");

        Assert.Equal("`myBucket`.`myScope`.`myCollection`", result);
    }

    [Fact]
    public void DelimitIdentifier_DottedWithBacktickInSegment_EscapesWithinSegment()
    {
        var result = _helper.DelimitIdentifier("bucket.sc`ope.collection");

        Assert.Equal("`bucket`.`sc``ope`.`collection`", result);
    }

    [Fact]
    public void DelimitIdentifier_StringBuilder_TwoPart_SplitsEachSegment()
    {
        var builder = new StringBuilder();

        _helper.DelimitIdentifier(builder, "bucket.scope");

        Assert.Equal("`bucket`.`scope`", builder.ToString());
    }

    [Fact]
    public void DelimitIdentifier_StringBuilder_ThreePart_SplitsAllThreeSegments()
    {
        var builder = new StringBuilder();

        _helper.DelimitIdentifier(builder, "default.blogs.personphoto");

        Assert.Equal("`default`.`blogs`.`personphoto`", builder.ToString());
    }

    [Fact]
    public void DelimitIdentifier_StringBuilder_DottedWithBacktick_EscapesWithinSegment()
    {
        var builder = new StringBuilder();

        _helper.DelimitIdentifier(builder, "bucket.sc`ope");

        Assert.Equal("`bucket`.`sc``ope`", builder.ToString());
    }

    [Fact]
    public void DelimitIdentifier_WithSchema_TwoPart_SplitsSchema()
    {
        // Two-argument overload: schema + name both dotted
        var result = _helper.DelimitIdentifier("collection", "bucket.scope");

        Assert.Equal("`bucket`.`scope`.`collection`", result);
    }
}
