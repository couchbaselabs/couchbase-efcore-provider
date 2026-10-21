using System.Runtime.CompilerServices;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// The per-query execution options a <c>ConsistentWith</c> and/or <c>WithScanConsistency</c> call
/// asked for, carried from <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/> (which
/// records them while translating the call) to <see cref="CouchbaseShapedQueryCompilingExpressionVisitor"/>
/// (which reads them when it builds the query's enumerable), through the one
/// <see cref="QueryCompilationContext"/> both visitors are created with.
/// </summary>
/// <remarks>
/// <para>
/// These are options of the whole query, not of a position in the operator chain, which is why they
/// are stored beside the translation instead of in it. An earlier design wrapped the query's shaper
/// in a marker node that the compile step stripped off again. That could only survive operators that
/// leave the shaper alone, so it forced the call to be the LAST operator, and worse, operators that
/// replace the shaper (predicate-free <c>Count()</c>, <c>LongCount()</c>, <c>Any()</c>) silently
/// discarded it, so the query ran with the context default: a stale read with no error. Because
/// nothing is attached to the expression tree here, every operator is unaffected and the options can
/// be applied anywhere in the chain.
/// </para>
/// <para>
/// A subquery is translated with the same <see cref="QueryCompilationContext"/>, so an option applied
/// to a subquery applies to the whole request the query is sent as (a query is one request).
/// If the same option is set more than once, the last call wins. They do not alter the generated
/// SQL++, only how it is executed.
/// </para>
/// </remarks>
internal sealed class CouchbaseQueryExecutionOptions
{
    private static readonly ConditionalWeakTable<QueryCompilationContext, CouchbaseQueryExecutionOptions> Options = new();

    /// <summary>
    /// The generated query-parameter name holding a <c>ConsistentWith</c> call's
    /// <see cref="MutationState"/>, or <see langword="null"/> if that was not called.
    /// </summary>
    public string? ConsistentWithParameterName { get; set; }

    /// <summary>
    /// The per-query scan consistency a <c>WithScanConsistency</c> call set, or
    /// <see langword="null"/> if that was not called (the context-wide setting then applies).
    /// </summary>
    public QueryScanConsistency? ScanConsistency { get; set; }

    /// <summary>Gets the options for <paramref name="context"/>, creating them on first use.</summary>
    public static CouchbaseQueryExecutionOptions GetOrCreate(QueryCompilationContext context)
        => Options.GetValue(context, static _ => new CouchbaseQueryExecutionOptions());

    /// <summary>Gets the options for <paramref name="context"/>, or <see langword="null"/> if none were set.</summary>
    public static CouchbaseQueryExecutionOptions? Find(QueryCompilationContext context)
        => Options.TryGetValue(context, out var options) ? options : null;
}
