namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Annotation names used by the <see cref="CouchbaseMetaAttribute"/>/<c>HasCouchbaseMeta</c>
/// META-field mechanism. Shared across the write path (skip in the document body), the read path
/// (render <c>META(alias).field</c> in generated SQL++), and the CAS-specific concurrency wiring.
/// </summary>
public static class CouchbaseMetaAnnotationNames
{
    /// <summary>
    /// The <see cref="CouchbaseMetaField"/> a property is sourced from, stored as its
    /// <see cref="string"/> name (e.g. <c>"Cas"</c>).
    /// </summary>
    public const string MetaField = "Couchbase:MetaField";
}
