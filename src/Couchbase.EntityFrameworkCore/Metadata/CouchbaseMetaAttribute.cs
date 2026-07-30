namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Sources this property's value from N1QL's <c>META()</c> function instead of a JSON document
/// field — see <see cref="CouchbaseMetaField"/> for the supported fields.
/// </summary>
/// <remarks>
/// The property becomes a server-computed shadow-style value: it is never written into the
/// document body, and its value is populated by the query engine (via <c>META(alias).field</c>)
/// whenever it's read. For <see cref="CouchbaseMetaField.Cas"/>, also call
/// <c>.IsConcurrencyToken()</c> (or apply <see cref="System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute"/>)
/// so EF Core treats it as an optimistic-concurrency token — the two are required together
/// deliberately, so this provider's CAS-specific write-path behavior (sending the value on
/// update/delete, translating a mismatch to <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>)
/// is never silently triggered just by adding <c>.IsConcurrencyToken()</c> to an unrelated property.
/// </remarks>
/// <example>
/// <code>
/// public class Order
/// {
///     public int Id { get; set; }
///
///     [CouchbaseMeta(CouchbaseMetaField.Cas)]
///     [ConcurrencyCheck]
///     public ulong Cas { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public class CouchbaseMetaAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance for the specified metadata field.
    /// </summary>
    /// <param name="field">The document-metadata field this property is sourced from.</param>
    public CouchbaseMetaAttribute(CouchbaseMetaField field)
    {
        Field = field;
    }

    /// <summary>
    /// Gets the document-metadata field this property is sourced from.
    /// </summary>
    public CouchbaseMetaField Field { get; }
}
