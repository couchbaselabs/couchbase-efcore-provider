namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// A document-metadata field exposed by N1QL's <c>META()</c> function
/// (<see href="https://docs.couchbase.com/server/current/n1ql/n1ql-language-reference/indexing-meta-info.html"/>),
/// for use with <see cref="CouchbaseMetaAttribute"/> and the <c>HasCouchbaseMeta</c> fluent API.
/// </summary>
public enum CouchbaseMetaField
{
    /// <summary>
    /// The document's key (<c>META(alias).id</c>). Read-only. Maps to a <see cref="string"/> property.
    /// </summary>
    Id,

    /// <summary>
    /// The document's CAS (compare-and-swap) value (<c>META(alias).cas</c>) — an opaque value that
    /// changes on every mutation, Couchbase's equivalent of a SQL rowversion. Maps to a
    /// <see cref="ulong"/> property. Combine with EF Core's own <c>.IsConcurrencyToken()</c> to use
    /// it as an optimistic-concurrency token.
    /// </summary>
    Cas,

    /// <summary>
    /// The document's expiration (TTL), as Unix epoch seconds (<c>META(alias).expiration</c>) — 0
    /// means no expiration. Read-only: this provider does not currently support setting a TTL on
    /// write. Maps to a <see cref="long"/> property.
    /// </summary>
    Expiration,
}

/// <summary>
/// The expected CLR type for each <see cref="CouchbaseMetaField"/>, shared between
/// <see cref="Conventions.CouchbaseMetaConvention"/> and the <c>HasCouchbaseMeta</c> fluent API so
/// both enforce the exact same validation.
/// </summary>
internal static class CouchbaseMetaFieldClrTypes
{
    public static Type Get(CouchbaseMetaField field) => field switch
    {
        CouchbaseMetaField.Id => typeof(string),
        CouchbaseMetaField.Cas => typeof(ulong),
        CouchbaseMetaField.Expiration => typeof(long),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown CouchbaseMetaField."),
    };

    /// <summary>
    /// The C# keyword name for <see cref="Get"/>'s result (e.g. <c>"ulong"</c>, not <c>Type.Name</c>'s
    /// <c>"UInt64"</c>), for clearer error messages.
    /// </summary>
    public static string GetDisplayName(CouchbaseMetaField field) => field switch
    {
        CouchbaseMetaField.Id => "string",
        CouchbaseMetaField.Cas => "ulong",
        CouchbaseMetaField.Expiration => "long",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown CouchbaseMetaField."),
    };
}
