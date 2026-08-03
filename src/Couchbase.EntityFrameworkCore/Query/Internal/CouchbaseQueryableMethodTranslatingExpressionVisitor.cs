using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
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
