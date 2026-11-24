using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Sets the provider's hint annotations (<see cref="CouchbaseQueryHintAnnotationNames"/>) on a
/// <see cref="TableExpression"/> in a query tree.
/// </summary>
internal static class TableHintAnnotations
{
    /// <summary>
    /// Returns <paramref name="selectExpression"/> with <paramref name="table"/> replaced by a copy
    /// that has the annotation <paramref name="name"/> set to <paramref name="value"/>.
    /// </summary>
    public static SelectExpression AnnotateTable(
        SelectExpression selectExpression, TableExpression table, string name, object? value)
        => (SelectExpression)new TableSwapExpressionVisitor(table, SetHintAnnotation(table, name, value))
            .Visit(selectExpression);

    /// <summary>
    /// Sets a hint annotation on <paramref name="table"/>, replacing (last-call-wins) whatever value
    /// it may already carry under <paramref name="name"/>, instead of the
    /// <see cref="InvalidOperationException"/> (EF Core's internal "duplicate annotation" guard)
    /// that <see cref="TableExpressionBase.AddAnnotation"/> throws when asked to add a second,
    /// different value under a name that's already set -- reachable simply by chaining
    /// <c>.UseIndex("a").UseIndex("b")</c> (or <c>.UseHash(...)</c> twice) on the same queryable.
    /// A single N1QL keyspace reference can carry only one <c>USE INDEX</c>/<c>USE HASH</c> clause,
    /// so "the last hint applied wins" is the only sensible repeat semantics -- matching how e.g.
    /// chaining <c>.OrderBy(...)</c> twice replaces the earlier ordering rather than erroring.
    /// </summary>
    public static TableExpression SetHintAnnotation(TableExpression table, string name, object? value)
    {
        var existing = table.FindAnnotation(name);
        if (existing is null || Equals(existing.Value, value))
        {
            // AddAnnotation itself already no-ops (returns an equal table) for a repeated,
            // identical value -- only a genuinely different value needs the rebuild below.
            return (TableExpression)table.AddAnnotation(name, value);
        }

        var annotations = new SortedDictionary<string, IAnnotation>(StringComparer.Ordinal);
        foreach (var annotation in table.GetAnnotations())
        {
            if (annotation.Name != name)
            {
                annotations[annotation.Name] = annotation;
            }
        }

        annotations[name] = new Annotation(name, value);

#pragma warning disable EF1001 // TableExpression's (alias, table, annotations) constructor is an internal API.
        return new TableExpression(table.Alias, table.Table, annotations);
#pragma warning restore EF1001
    }

    /// <summary>
    /// Replaces a single <see cref="TableExpressionBase"/> reference (by-reference, not by value
    /// equality) throughout an expression tree. Relies on <see cref="SelectExpression"/>'s own
    /// <c>VisitChildren</c> to correctly propagate the swap through its <c>Tables</c> list (mutating
    /// in place while the select is still in its pre-projection-finalization mutable state) -- the
    /// same mechanism EF Core's own internal rewrite passes (e.g. the nullability processor) use for
    /// tree-local node replacement, needing no EF1001-marked internal API.
    /// </summary>
    private sealed class TableSwapExpressionVisitor(TableExpressionBase oldTable, TableExpressionBase newTable) : ExpressionVisitor
    {
        [return: NotNullIfNotNull(nameof(node))]
        public override Expression? Visit(Expression? node)
            => ReferenceEquals(node, oldTable) ? newTable : base.Visit(node);
    }
}
