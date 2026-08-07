namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Query-tree annotation names used by the <c>UseIndex</c>/<c>UseHash</c> per-query N1QL hint
/// mechanism (<see cref="Extensions.CouchbaseQueryableExtensions"/>). Shared between
/// <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/> (which stashes a hint as a
/// <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.TableExpressionBase"/> annotation
/// during translation) and <see cref="CouchbaseQuerySqlGenerator"/> (which reads it back to decide
/// what to render).
/// </summary>
internal static class CouchbaseQueryHintAnnotationNames
{
    /// <summary>
    /// A <c>(string? IndexName, Extensions.CouchbaseIndexType Type)</c> tuple recording a
    /// <c>UseIndex</c> hint for the annotated table.
    /// </summary>
    public const string UseIndex = "Couchbase:UseIndex";

    /// <summary>
    /// A <see cref="Extensions.CouchbaseHashHintType"/> recording a <c>UseHash</c> hint for the
    /// annotated table.
    /// </summary>
    public const string UseHash = "Couchbase:UseHash";
}
