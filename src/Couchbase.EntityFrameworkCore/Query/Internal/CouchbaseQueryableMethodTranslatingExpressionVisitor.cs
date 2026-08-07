using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
    private static readonly MethodInfo UseIndexMethodInfo = CouchbaseQueryableExtensions.UseIndexMethodInfo;
    private static readonly MethodInfo UseHashMethodInfo = CouchbaseQueryableExtensions.UseHashMethodInfo;

    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly SqlAliasManager _sqlAliasManager;

    public CouchbaseQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _typeMappingSource = relationalDependencies.TypeMappingSource;
        _sqlAliasManager = queryCompilationContext.SqlAliasManager;
    }

    protected CouchbaseQueryableMethodTranslatingExpressionVisitor(
        CouchbaseQueryableMethodTranslatingExpressionVisitor parentVisitor)
        : base(parentVisitor)
    {
        _typeMappingSource = parentVisitor._typeMappingSource;
        _sqlAliasManager = parentVisitor._sqlAliasManager;
    }

    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor()
        => new CouchbaseQueryableMethodTranslatingExpressionVisitor(this);

    /// <summary>
    /// Intercepts <see cref="CouchbaseQueryableExtensions.UseIndex{TEntity}"/>/
    /// <see cref="CouchbaseQueryableExtensions.UseHash{TEntity}"/> calls -- neither is a real LINQ
    /// operator the base class knows about, so without this override the base implementation would
    /// throw "translation failed" for any query using them.
    /// </summary>
    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    {
        var method = methodCallExpression.Method;
        if (method.IsGenericMethod && method.DeclaringType == typeof(CouchbaseQueryableExtensions))
        {
            var genericMethod = method.GetGenericMethodDefinition();
            if (genericMethod == UseIndexMethodInfo)
            {
                return TranslateUseIndex(methodCallExpression);
            }

            if (genericMethod == UseHashMethodInfo)
            {
                return TranslateUseHash(methodCallExpression);
            }
        }

        return base.VisitMethodCall(methodCallExpression);
    }

    /// <summary>
    /// Stashes a <c>UseIndex</c> hint as a <see cref="CouchbaseQueryHintAnnotationNames.UseIndex"/>
    /// annotation on the sole <see cref="TableExpression"/> of the visited source, for
    /// <see cref="CouchbaseQuerySqlGenerator"/> to render as N1QL's <c>USE INDEX(...)</c>. Only the
    /// simple "root queryable, nothing composed yet" shape <c>UseIndex</c> is documented to require
    /// is recognized (see <see cref="IsUnpushedDownTable"/>); anything else silently ignores the
    /// hint (an optimizer nudge, not a correctness requirement) rather than failing the whole query
    /// or -- the bug this guard specifically prevents -- silently annotating a table that a later
    /// pushdown buries inside a subquery, rendering the hint on the wrong FROM term. Chaining
    /// <c>.UseIndex(...)</c> more than once on the same queryable is last-call-wins -- see
    /// <see cref="SetHintAnnotation"/>.
    /// </summary>
    private Expression TranslateUseIndex(MethodCallExpression methodCallExpression)
    {
        var source = Visit(methodCallExpression.Arguments[0]);
        if (source is ShapedQueryExpression { QueryExpression: SelectExpression selectExpression } shapedQueryExpression
            && IsUnpushedDownTable(selectExpression, out var table))
        {
            var indexName = (string?)((ConstantExpression)methodCallExpression.Arguments[1]).Value;
            var indexType = (CouchbaseIndexType)((ConstantExpression)methodCallExpression.Arguments[2]).Value!;
            var annotatedTable = SetHintAnnotation(table, CouchbaseQueryHintAnnotationNames.UseIndex, (indexName, indexType));
            var newSelectExpression = (SelectExpression)new TableSwapExpressionVisitor(table, annotatedTable).Visit(selectExpression);
            return shapedQueryExpression.UpdateQueryExpression(newSelectExpression);
        }

        return source!;
    }

    /// <summary>
    /// Stashes a <c>UseHash</c> hint as a <see cref="CouchbaseQueryHintAnnotationNames.UseHash"/>
    /// annotation on the sole <see cref="TableExpression"/> of the visited source (the join's inner
    /// sequence, per <c>UseHash</c>'s documented usage), for <see cref="CouchbaseQuerySqlGenerator"/>
    /// to render as N1QL's <c>USE HASH(BUILD|PROBE)</c> once the base class wraps this table in the
    /// actual join expression. Same "ignore the hint if the shape doesn't match" fallback (see
    /// <see cref="IsUnpushedDownTable"/>), and same last-call-wins repeat semantics, as
    /// <see cref="TranslateUseIndex"/>.
    /// </summary>
    private Expression TranslateUseHash(MethodCallExpression methodCallExpression)
    {
        var source = Visit(methodCallExpression.Arguments[0]);
        if (source is ShapedQueryExpression { QueryExpression: SelectExpression selectExpression } shapedQueryExpression
            && IsUnpushedDownTable(selectExpression, out var table))
        {
            var hashType = (CouchbaseHashHintType)((ConstantExpression)methodCallExpression.Arguments[1]).Value!;
            var annotatedTable = SetHintAnnotation(table, CouchbaseQueryHintAnnotationNames.UseHash, hashType);
            var newSelectExpression = (SelectExpression)new TableSwapExpressionVisitor(table, annotatedTable).Visit(selectExpression);
            return shapedQueryExpression.UpdateQueryExpression(newSelectExpression);
        }

        return source!;
    }

    /// <summary>
    /// True if <paramref name="selectExpression"/> is still exactly its bare, single, un-composed
    /// table -- i.e. annotating that <paramref name="table"/> now is guaranteed to still be
    /// annotating it once query generation finishes, because nothing about this
    /// <paramref name="selectExpression"/> will force EF Core to bury it inside a pushed-down
    /// subquery later. Mirrors, property-for-property, the exact pushdown condition EF Core's own
    /// <c>SelectExpression.AddJoin</c> uses (<c>innerSelect.Limit != null || innerSelect.Offset !=
    /// null || innerSelect.IsDistinct || innerSelect.Predicate != null || innerSelect.Tables.Count
    /// &gt; 1 || innerSelect.GroupBy.Count > 0</c>) when deciding whether a join's inner sequence
    /// needs subquery pushdown -- not a guess at what "simple" means. Without this check, a hint
    /// applied to e.g. <c>inner.Where(...).UseHash(...)</c> or <c>inner.Take(1).UseHash(...)</c>
    /// used as a join's inner sequence would still match a looser "single <see cref="TableExpression"/>"
    /// pattern and get annotated, but <c>AddJoin</c> then pushes that exact table down into a new
    /// subquery wrapping it -- leaving the annotation on the table now nested INSIDE that subquery,
    /// so <see cref="CouchbaseQuerySqlGenerator.VisitTable"/> renders <c>USE HASH(...)</c> on the
    /// subquery's own inner <c>FROM</c> term instead of on the outer join's keyspace reference,
    /// producing a misplaced (and likely invalid) hint instead of silently doing nothing.
    /// </summary>
    private static bool IsUnpushedDownTable(SelectExpression selectExpression, [NotNullWhen(true)] out TableExpression? table)
    {
        if (selectExpression is
            {
                Tables: [TableExpression singleTable],
                Predicate: null,
                Limit: null,
                Offset: null,
                IsDistinct: false,
                GroupBy.Count: 0,
            })
        {
            table = singleTable;
            return true;
        }

        table = null;
        return false;
    }

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
    private static TableExpression SetHintAnnotation(TableExpression table, string name, object? value)
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

    /// <summary>
    /// Expands a scalar "primitive collection" property (e.g. <c>List&lt;string&gt; Tags</c>
    /// mapped directly to a JSON array field, not via <c>OwnsMany</c>) into a queryable rowset,
    /// modeled on SQLite's <c>TranslatePrimitiveCollection</c> (<c>json_each</c>-based)
    /// implementation but rendering as a bare correlated <c>FROM arrayExpr AS alias</c> term (no
    /// literal <c>UNNEST</c> keyword) -- see <see cref="CouchbaseUnnestExpression"/>'s remarks for
    /// why. Once this returns a well-formed <see cref="ShapedQueryExpression"/>, every
    /// order-independent LINQ collection operator (.Where/.Count/.Contains/.Any/...) composes on
    /// top of it for free via EF Core's own generic, provider-agnostic machinery.
    /// </summary>
    /// <remarks>
    /// Unlike SQLite/SqlServer, this carries no positional/ordinal column -- a live-cluster spike
    /// confirmed this Couchbase Server version rejects <c>UNNEST ... AT alias</c> syntax entirely,
    /// so there's no way to get a deterministic array position out of the unnest rowset itself.
    /// Element indexing is instead handled entirely by <see cref="TranslateElementAtOrDefault"/>
    /// below, rendering N1QL's native array-subscript syntax directly rather than relying on the
    /// base class's generic Skip+Limit-over-an-ordered-rowset approach (which requires exactly the
    /// ordinal column this provider cannot produce).
    /// </remarks>
    protected override ShapedQueryExpression? TranslatePrimitiveCollection(
        SqlExpression sqlExpression,
        IProperty? property,
        string tableAlias)
    {
        // Parameter-rooted primitive collections (no mapped IProperty, e.g. a local List<T>
        // captured in a closure) are explicitly out of scope for v1 -- see the plan's scope notes.
        if (property?.GetElementType() is not { } elementType)
        {
            return null;
        }

        var elementClrType = elementType.ClrType;
        var elementTypeMapping = (RelationalTypeMapping?)sqlExpression.TypeMapping?.ElementTypeMapping
            ?? _typeMappingSource.FindMapping(elementClrType);

        var unnestExpression = new CouchbaseUnnestExpression(tableAlias, sqlExpression);
        var valueExpression = new CouchbaseUnnestValueExpression(
            tableAlias, elementType.IsNullable, elementClrType.UnwrapNullableType(), elementTypeMapping);

#pragma warning disable EF1001 // SelectExpression's (tables, projection, identifier, aliasManager) constructor is an internal API.
        var selectExpression = new SelectExpression(
            [unnestExpression],
            valueExpression,
            identifier: [],
            _sqlAliasManager);
#pragma warning restore EF1001

        Expression shaperExpression = new ProjectionBindingExpression(
            selectExpression, new ProjectionMember(), elementClrType.MakeNullable());

        if (elementClrType != shaperExpression.Type)
        {
            shaperExpression = Expression.Convert(shaperExpression, elementClrType);
        }

        return new ShapedQueryExpression(selectExpression, shaperExpression);
    }

    /// <summary>
    /// Renders N1QL's native <c>arrayExpr[indexExpr]</c> subscript directly, rather than the base
    /// class's generic OFFSET/LIMIT-over-an-ordered-rowset approach -- see the remarks on
    /// <see cref="TranslatePrimitiveCollection"/> for why that approach isn't available here.
    /// Only handles the exact, untouched shape <see cref="TranslatePrimitiveCollection"/> itself
    /// produces (a bare <see cref="CouchbaseUnnestExpression"/>, no predicate/ordering/offset/limit
    /// applied yet). Any OTHER composition over a <see cref="CouchbaseUnnestExpression"/> source --
    /// <c>.Where(...).ElementAt(i)</c>, <c>.OrderBy(...).ElementAt(i)</c> -- returns
    /// <see langword="null"/> outright rather than falling back to the base implementation, which
    /// would still render as syntactically valid but semantically nondeterministic N1QL for that
    /// source (no <c>AT alias</c> positional binding exists for this provider's unnest rendering,
    /// so there's no real row order to skip over) -- silently returning a wrong element instead of
    /// failing loudly. A non-unnest source (e.g. an <c>OwnsMany</c> navigation's FK-joined
    /// <see cref="TableExpression"/>) still falls back to the base implementation, which
    /// <see cref="CouchbaseQuerySqlGenerator"/> has its own chance to render correctly (or reject)
    /// at the SQL-generation layer.
    /// </summary>
    protected override ShapedQueryExpression? TranslateElementAtOrDefault(
        ShapedQueryExpression source, Expression index, bool returnDefault)
    {
        if (source.QueryExpression is not SelectExpression { Tables: [CouchbaseUnnestExpression] } unnestSourceSelect)
        {
            // Not a primitive-collection source at all (e.g. an OwnsMany navigation's FK-joined
            // TableExpression) -- fall back to the base implementation, which
            // CouchbaseQuerySqlGenerator has its own chance to render correctly (or reject) at the
            // SQL-generation layer.
            return base.TranslateElementAtOrDefault(source, index, returnDefault);
        }

        if (unnestSourceSelect is not
            {
                Tables: [CouchbaseUnnestExpression unnestExpression],
                Predicate: null,
                Orderings: [],
                Offset: null,
                Limit: null,
                IsDistinct: false,
            } selectExpression)
        {
            // A primitive-collection source with extra composition already applied --
            // .Where(...).ElementAt(i), .OrderBy(...).ElementAt(i). Must return null outright
            // rather than fall back: the base OFFSET/LIMIT implementation would still render as
            // syntactically valid N1QL over this source (CouchbaseQuerySqlGenerator.GenerateUnnest
            // handles it), but semantically nondeterministic -- no AT alias positional binding
            // exists for this provider's unnest rendering, so there's no real row order to skip
            // over, and it would silently return a wrong element instead of failing loudly.
            return null;
        }

        // The SelectExpression built by TranslatePrimitiveCollection uses the single-scalar-
        // projection constructor, which stores the projection via the projection-mapping
        // dictionary (read back through GetProjection), not the finalized Projection list --
        // that list only populates once ApplyProjection() runs, later in the pipeline.
        var sourceShaperExpression = source.ShaperExpression;
        if (sourceShaperExpression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            sourceShaperExpression = unary.Operand;
        }

        if (sourceShaperExpression is not ProjectionBindingExpression projectionBindingExpression)
        {
            return null;
        }

        var translatedIndex = TranslateExpression(index);
        if (translatedIndex == null)
        {
            return null;
        }

        var valueProjection = (SqlExpression)selectExpression.GetProjection(projectionBindingExpression);
        var arrayIndexExpression = new CouchbaseArrayIndexExpression(
            unnestExpression.ArrayExpression, translatedIndex, valueProjection.Type, valueProjection.TypeMapping);

#pragma warning disable EF1001 // SelectExpression(projection, aliasManager) constructor is an internal API.
        var scalarSelectExpression = new SelectExpression(arrayIndexExpression, _sqlAliasManager);
#pragma warning restore EF1001

        var resultShaperExpression = new ProjectionBindingExpression(
            scalarSelectExpression, new ProjectionMember(), valueProjection.Type.MakeNullable());

        // Must be non-Enumerable for RelationalSqlTranslatingExpressionVisitor.VisitExtension's
        // ShapedQueryExpression case to fold this into a scalar subquery -- the 2-arg
        // ShapedQueryExpression constructor defaults to Enumerable, which that fold explicitly
        // excludes (it's meant for genuine collection-returning subqueries, not `.ElementAt()`'s
        // single-value result). The 3-arg (queryExpression, shaperExpression, cardinality)
        // constructor is private; UpdateResultCardinality is the public way to set it.
        return new ShapedQueryExpression(scalarSelectExpression, resultShaperExpression)
            .UpdateResultCardinality(ResultCardinality.SingleOrDefault);
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
