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

    /// <summary>
    /// The document's flags (<c>META(alias).flags</c>) — an opaque 32-bit value the SDK's KV layer
    /// uses to record the document's datatype (e.g. JSON). Read-only; this provider never sets it
    /// itself. Maps to a <see cref="uint"/> property.
    /// </summary>
    /// <remarks>
    /// <b>Known limitation:</b> do not also project <see cref="Expiration"/> in the same query as
    /// <c>Flags</c> (e.g. both as <c>[CouchbaseMeta]</c> properties on the same queried entity).
    /// Confirmed against a live cluster (bypassing this provider's SQL generation and reader
    /// entirely, inspecting the raw N1QL response) that the Couchbase Server query engine itself
    /// returns <c>0</c> for <c>flags</c> whenever <c>META(alias).flags</c> and
    /// <c>META(alias).expiration</c> are both projected in one <c>SELECT</c> — this is a
    /// server-side query-engine bug, not something this provider causes or can work around
    /// client-side. Querying <c>Flags</c> alone, or alongside <c>Cas</c>/<c>Id</c>/<c>Type</c>
    /// (any combination that excludes <c>Expiration</c>), reads back correctly.
    /// </remarks>
    Flags,

    /// <summary>
    /// The document's type (<c>META(alias).type</c>) — e.g. <c>"json"</c> for a JSON document.
    /// Read-only. Maps to a <see cref="string"/> property.
    /// </summary>
    Type,
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
        CouchbaseMetaField.Flags => typeof(uint),
        CouchbaseMetaField.Type => typeof(string),
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
        CouchbaseMetaField.Flags => "uint",
        CouchbaseMetaField.Type => "string",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown CouchbaseMetaField."),
    };
}
