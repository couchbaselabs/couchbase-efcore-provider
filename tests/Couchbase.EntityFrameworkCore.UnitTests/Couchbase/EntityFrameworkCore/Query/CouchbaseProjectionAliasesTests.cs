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
    public void MakeUnique_LaterDistinctLiteral_IsNotConsumedByEarlierDuplicate()
    {
        // The duplicate "rating" at index 1 must NOT take "rating0" — that literal appears later
        // (index 2) as a distinct alias and must keep its own slot. The duplicate skips to
        // "rating1" instead.
        var result = CouchbaseProjectionAliases.MakeUnique(["rating", "rating", "rating0"]);
        Assert.Equal(["rating", "rating1", "rating0"], result);
    }

    [Fact]
    public void MakeUnique_CaseOnlyDuplicate_IsSuffixed()
    {
        // CouchbaseDbDataReader keys its alias->ordinal map with OrdinalIgnoreCase, so aliases
        // differing only by case collide at read time. MakeUnique must treat them as duplicates;
        // the base name's original casing is preserved in the suffix.
        var result = CouchbaseProjectionAliases.MakeUnique(["rating", "Rating"]);
        Assert.Equal(["rating", "Rating0"], result);
    }

    [Fact]
    public void MakeUnique_CaseInsensitiveReservation_SkipsCaseOnlyLiteral()
    {
        // The duplicate "rating" must not take "RATING0" (a distinct literal that appears later but
        // differs only by case) — it skips to "rating1".
        var result = CouchbaseProjectionAliases.MakeUnique(["rating", "rating", "RATING0"]);
        Assert.Equal(["rating", "rating1", "RATING0"], result);
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
        // reader relies on for a 1:1 ordinal->key mapping). The input includes a case-only
        // duplicate ("A") so the distinctness check must use the same OrdinalIgnoreCase
        // semantics as CouchbaseDbDataReader's alias map — otherwise a case-only collision in
        // the output would slip past a case-sensitive comparer.
        var result = CouchbaseProjectionAliases.MakeUnique(
            ["a", "a", "b", "A", "b", "c", "a0"]);
        Assert.Equal(result.Length, new HashSet<string>(result, StringComparer.OrdinalIgnoreCase).Count);
    }
}
