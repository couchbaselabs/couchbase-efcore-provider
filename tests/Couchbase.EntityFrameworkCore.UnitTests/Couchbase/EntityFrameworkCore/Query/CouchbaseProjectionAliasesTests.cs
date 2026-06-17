using Couchbase.EntityFrameworkCore.Query.Internal;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Tests for <see cref="CouchbaseProjectionAliases.MakeUnique"/>, the alias-uniquification used
/// to give each N1QL projection a distinct result-object key. Distinct keys are required because
/// a N1QL row is a JSON object keyed by alias — two projections sharing a name (e.g. a collection
/// Include where principal and dependent both expose <c>rating</c> / <c>blogId</c>) would collide
/// on a single key and the reader would map two shaper ordinals to the same value.
/// </summary>
public class CouchbaseProjectionAliasesTests
{
    [Fact]
    public void MakeUnique_NoDuplicates_ReturnsInputUnchanged()
    {
        var result = CouchbaseProjectionAliases.MakeUnique(["blogId", "ownerId", "rating", "url"]);
        Assert.Equal(["blogId", "ownerId", "rating", "url"], result);
    }

    [Fact]
    public void MakeUnique_SingleDuplicate_SuffixesSecondOccurrence()
    {
        // The collision case from the filtered-Include bug: Blog and Post both project
        // blogId and rating. First occurrence kept; second suffixed.
        var result = CouchbaseProjectionAliases.MakeUnique(
            ["blogId", "ownerId", "rating", "url", "postId", "authorId", "blogId", "content", "rating", "title"]);

        Assert.Equal(
            ["blogId", "ownerId", "rating", "url", "postId", "authorId", "blogId0", "content", "rating0", "title"],
            result);
    }

    [Fact]
    public void MakeUnique_RepeatedDuplicates_IncrementSuffix()
    {
        var result = CouchbaseProjectionAliases.MakeUnique(["rating", "rating", "rating", "rating"]);
        Assert.Equal(["rating", "rating0", "rating1", "rating2"], result);
    }

    [Fact]
    public void MakeUnique_PreExistingSuffixedName_DoesNotCollide()
    {
        // "rating0" already present, so the suffixed form of the duplicate "rating" must skip it
        // and pick the next free candidate.
        var result = CouchbaseProjectionAliases.MakeUnique(["rating", "rating0", "rating"]);
        Assert.Equal(["rating", "rating0", "rating1"], result);
    }

    [Fact]
    public void MakeUnique_FirstOccurrenceAlwaysVerbatim()
    {
        // Order is preserved and only later occurrences are renamed — never the first.
        var result = CouchbaseProjectionAliases.MakeUnique(["id", "name", "id"]);
        Assert.Equal("id", result[0]);
        Assert.Equal("id0", result[2]);
    }

    [Fact]
    public void MakeUnique_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(CouchbaseProjectionAliases.MakeUnique([]));
    }

    [Fact]
    public void MakeUnique_AllElementsDistinct()
    {
        // Whatever the input, the output must contain no duplicates (the core invariant the
        // reader relies on for a 1:1 ordinal->key mapping).
        var result = CouchbaseProjectionAliases.MakeUnique(
            ["a", "a", "b", "a", "b", "c", "a0"]);
        Assert.Equal(result.Length, new HashSet<string>(result).Count);
    }
}
