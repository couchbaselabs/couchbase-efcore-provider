namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Query-tree annotation names used by the <c>UseIndex</c>/<c>UseHash</c> per-query N1QL hint
/// mechanism (<see cref="Extensions.CouchbaseQueryableExtensions"/>). Shared between
/// <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/> (which stashes a hint as a
/// <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.TableExpressionBase"/> annotation
/// during translation) and <see cref="CouchbaseQuerySqlGenerator"/> (which reads it back to decide
/// what to render). <c>ConsistentWith</c> and <c>WithScanConsistency</c> do NOT use this mechanism -- see
/// <see cref="CouchbaseQueryExecutionOptions"/> for why (this class's own
/// <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression"/> annotation
/// support turned out to be unimplemented in EF Core 10) and what it uses instead.
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

    /// <summary>
    /// Marks the table of a <c>Count()</c>/<c>LongCount()</c> over one keyspace.
    /// <para>
    /// Without a predicate, the query service answers <c>COUNT(*)</c> from the collection's document
    /// count (a <c>CountScan</c>), which ignores the scan consistency and can be stale just after
    /// documents are deleted and inserted. So when the query needs read-your-writes consistency,
    /// <see cref="CouchbaseQuerySqlGenerator"/> gives such a <c>COUNT</c> the predicate
    /// <c>WHERE META(d).id IS NOT MISSING</c>: true for every document, but it makes the count go
    /// through the primary index, which honours the consistency. A <c>NotBounded</c> query keeps the
    /// faster <c>CountScan</c>.
    /// </para>
    /// <para>
    /// Whether the query needs consistency is only known when it runs: a <c>WithScanConsistency</c>
    /// or <c>ConsistentWith</c> anywhere in the query applies to the whole query, and a
    /// <c>ConsistentWith</c> value can be null. So the translator sets a
    /// <see cref="ConsistentCountMarker"/>, and <see cref="CouchbaseParameterBasedSqlProcessor"/>
    /// replaces it with a <see cref="bool"/> when the query runs.
    /// </para>
    /// </summary>
    public const string ConsistentCount = "Couchbase:ConsistentCount";
}
