using Couchbase.EntityFrameworkCore.Metadata;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Metadata;

public class KeyspaceMappingTests
{
    #region CouchbaseKeyspace Record Tests

    [Fact]
    public void CouchbaseKeyspace_ToString_ReturnsStandardFormat()
    {
        var keyspace = new CouchbaseKeyspace("bucket", "scope", "collection");
        Assert.Equal("bucket.scope.collection", keyspace.ToString());
    }

    [Fact]
    public void CouchbaseKeyspace_ToSqlString_ReturnsBacktickFormat()
    {
        var keyspace = new CouchbaseKeyspace("bucket", "scope", "collection");
        Assert.Equal("`bucket`.`scope`.`collection`", keyspace.ToSqlString());
    }

    [Fact]
    public void CouchbaseKeyspace_Parse_ParsesStandardFormat()
    {
        var keyspace = CouchbaseKeyspace.Parse("my-bucket.my-scope.my-collection");

        Assert.Equal("my-bucket", keyspace.Bucket);
        Assert.Equal("my-scope", keyspace.Scope);
        Assert.Equal("my-collection", keyspace.Collection);
    }

    [Fact]
    public void CouchbaseKeyspace_Parse_TrimsBackticks()
    {
        var keyspace = CouchbaseKeyspace.Parse("`my-bucket`.`my-scope`.`my-collection`");

        Assert.Equal("my-bucket", keyspace.Bucket);
        Assert.Equal("my-scope", keyspace.Scope);
        Assert.Equal("my-collection", keyspace.Collection);
    }

    [Fact]
    public void CouchbaseKeyspace_Parse_ThrowsOnInvalidFormat()
    {
        Assert.Throws<ArgumentException>(() => CouchbaseKeyspace.Parse("invalid"));
        Assert.Throws<ArgumentException>(() => CouchbaseKeyspace.Parse("only.two"));
        Assert.Throws<ArgumentException>(() => CouchbaseKeyspace.Parse("too.many.parts.here"));
    }

    [Fact]
    public void CouchbaseKeyspace_TryParse_ReturnsTrueForValidFormat()
    {
        var result = CouchbaseKeyspace.TryParse("bucket.scope.collection", out var keyspace);

        Assert.True(result);
        Assert.NotNull(keyspace);
        Assert.Equal("bucket", keyspace.Value.Bucket);
        Assert.Equal("scope", keyspace.Value.Scope);
        Assert.Equal("collection", keyspace.Value.Collection);
    }

    [Fact]
    public void CouchbaseKeyspace_TryParse_ReturnsFalseForInvalidFormat()
    {
        Assert.False(CouchbaseKeyspace.TryParse("invalid", out _));
        Assert.False(CouchbaseKeyspace.TryParse("only.two", out _));
        Assert.False(CouchbaseKeyspace.TryParse(null, out _));
        Assert.False(CouchbaseKeyspace.TryParse("", out _));
    }

    [Fact]
    public void CouchbaseKeyspace_TryParse_ReturnsFalseForEmptyParts()
    {
        // Empty bucket
        Assert.False(CouchbaseKeyspace.TryParse(".scope.collection", out _));
        // Empty scope
        Assert.False(CouchbaseKeyspace.TryParse("bucket..collection", out _));
        // Empty collection
        Assert.False(CouchbaseKeyspace.TryParse("bucket.scope.", out _));
        // Multiple empty parts
        Assert.False(CouchbaseKeyspace.TryParse("..", out _));
        // Backticks only (empty after trim)
        Assert.False(CouchbaseKeyspace.TryParse("`.`.``.collection", out _));
        Assert.False(CouchbaseKeyspace.TryParse("bucket.``.collection", out _));
    }

    [Fact]
    public void CouchbaseKeyspace_Constructor_ThrowsOnNullOrEmpty()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspace(null!, "scope", "collection"));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspace("bucket", null!, "collection"));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspace("bucket", "scope", null!));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspace("", "scope", "collection"));
    }

    [Fact]
    public void CouchbaseKeyspace_Equality_WorksCorrectly()
    {
        var keyspace1 = new CouchbaseKeyspace("bucket", "scope", "collection");
        var keyspace2 = new CouchbaseKeyspace("bucket", "scope", "collection");
        var keyspace3 = new CouchbaseKeyspace("bucket", "scope", "other");

        Assert.Equal(keyspace1, keyspace2);
        Assert.NotEqual(keyspace1, keyspace3);
    }

    #endregion

    #region CouchbaseKeyspaceAttribute Tests

    [Fact]
    public void CouchbaseKeyspaceAttribute_WithCollectionOnly_SetsCollectionWithNoScopeOverride()
    {
        var attribute = new CouchbaseKeyspaceAttribute("users");

        Assert.Equal("users", attribute.Collection);
        Assert.Null(attribute.Scope);
        Assert.False(attribute.HasScopeOverride);
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_WithScopeAndCollection_SetsBothValuesWithScopeOverride()
    {
        var attribute = new CouchbaseKeyspaceAttribute("custom-scope", "products");

        Assert.Equal("products", attribute.Collection);
        Assert.Equal("custom-scope", attribute.Scope);
        Assert.True(attribute.HasScopeOverride);
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_HasScopeOverride_FalseForCollectionOnlyConstructor()
    {
        var attribute = new CouchbaseKeyspaceAttribute("my-collection");

        Assert.False(attribute.HasScopeOverride);
        Assert.Null(attribute.Scope);
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_HasScopeOverride_TrueForScopeAndCollectionConstructor()
    {
        var attribute = new CouchbaseKeyspaceAttribute("my-scope", "my-collection");

        Assert.True(attribute.HasScopeOverride);
        Assert.Equal("my-scope", attribute.Scope);
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_WithCollectionOnly_HasNullBucket()
    {
        var attribute = new CouchbaseKeyspaceAttribute("users");

        Assert.Null(attribute.Bucket);
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_WithBucketScopeAndCollection_SetsAllValues()
    {
        var attribute = new CouchbaseKeyspaceAttribute("secondary", "custom-scope", "products");

        Assert.Equal("secondary", attribute.Bucket);
        Assert.Equal("custom-scope", attribute.Scope);
        Assert.Equal("products", attribute.Collection);
        Assert.True(attribute.HasScopeOverride);
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_ThrowsOnNullBucketScopeOrCollection()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute(null!, "scope", "collection"));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute("bucket", null!, "collection"));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute("bucket", "scope", null!));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute("", "scope", "collection"));
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_ThrowsOnNullCollection()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute(null!));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute(""));
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_ThrowsOnNullScopeOrCollection()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute(null!, "collection"));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute("scope", null!));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute("", "collection"));
        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseKeyspaceAttribute("scope", ""));
    }

    #endregion

    #region Integration Format Tests

    [Fact]
    public void CouchbaseKeyspace_RoundTrip_PreservesValues()
    {
        // Create keyspace, convert to string, parse back
        var original = new CouchbaseKeyspace("my-bucket", "my-scope", "my-collection");
        var asString = original.ToString();
        var parsed = CouchbaseKeyspace.Parse(asString);

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void CouchbaseKeyspace_Format_MatchesExpectedPattern()
    {
        // Verify the format is Bucket.Scope.Collection (standard Couchbase format)
        var keyspace = new CouchbaseKeyspace("travel-sample", "inventory", "airline");

        Assert.Equal("travel-sample.inventory.airline", keyspace.ToString());
        Assert.Equal("`travel-sample`.`inventory`.`airline`", keyspace.ToSqlString());
    }

    [Fact]
    public void CouchbaseKeyspace_WithSpecialCharacters_HandlesCorrectly()
    {
        // Bucket/scope/collection names can contain hyphens and underscores
        var keyspace = new CouchbaseKeyspace("my-bucket", "my_scope", "my-collection_v2");

        Assert.Equal("my-bucket", keyspace.Bucket);
        Assert.Equal("my_scope", keyspace.Scope);
        Assert.Equal("my-collection_v2", keyspace.Collection);
    }

    [Fact]
    public void CouchbaseKeyspace_TryParse_WithBackticks_TrimsCorrectly()
    {
        // Verify backticks are trimmed when parsing
        var result = CouchbaseKeyspace.TryParse("`bucket`.`scope`.`collection`", out var keyspace);

        Assert.True(result);
        Assert.Equal("bucket", keyspace!.Value.Bucket);
        Assert.Equal("scope", keyspace.Value.Scope);
        Assert.Equal("collection", keyspace.Value.Collection);
    }

    [Fact]
    public void CouchbaseKeyspace_TryParse_WithMixedBackticks_TrimsCorrectly()
    {
        // Some parts have backticks, some don't
        var result = CouchbaseKeyspace.TryParse("`bucket`.scope.`collection`", out var keyspace);

        Assert.True(result);
        Assert.Equal("bucket", keyspace!.Value.Bucket);
        Assert.Equal("scope", keyspace.Value.Scope);
        Assert.Equal("collection", keyspace.Value.Collection);
    }

    #endregion
}
