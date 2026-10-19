using System.Linq.Expressions;
using Couchbase.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Wraps a <see cref="Microsoft.EntityFrameworkCore.Query.ShapedQueryExpression.ShaperExpression"/>
/// to carry per-query execution options from <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/>
/// (which creates it, for a <c>ConsistentWith</c> and/or <c>WithScanConsistency</c> call) to
/// <see cref="CouchbaseShapedQueryCompilingExpressionVisitor"/> (which strips it back off, as the
/// very first thing it does, before any of its own shaper-shape checks run).
/// </summary>
/// <remarks>
/// Chosen over annotating the <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression"/>
/// itself (the way <c>UseIndex</c>/<c>UseHash</c> annotate a child
/// <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.TableExpression"/>) because
/// <c>SelectExpression</c>'s own <c>WithAnnotations</c> override is unimplemented in EF Core 10 --
/// it throws <see cref="System.Diagnostics.UnreachableException"/> unconditionally (confirmed
/// empirically: a unit test hit this exact throw before this design was adopted). Wrapping the
/// shaper instead needs no annotation support at all -- it only relies on
/// <see cref="Microsoft.EntityFrameworkCore.Query.ShapedQueryExpression.UpdateShaperExpression"/>,
/// a plain, fully-supported public API.
/// <para>
/// This also enforces the "apply last" requirement documented on
/// <see cref="Extensions.CouchbaseQueryableExtensions.ConsistentWith{TEntity}"/> and
/// <see cref="Extensions.CouchbaseQueryableExtensions.WithScanConsistency{TEntity}"/> -- but by
/// throwing, not by silently dropping the option (confirmed by a regression test, correcting an
/// earlier, untested assumption). In practice this means these calls must be the LAST operators
/// applied: even operators like <c>.Where()</c>/<c>.OrderBy()</c>, which only need the
/// <c>QueryExpression</c> conceptually, still bind their lambda parameter against the shaper's
/// shape as part of translation -- and since this wrapper isn't a shape any base-class translation
/// logic recognizes, that binding fails with EF Core's own "could not be translated"
/// <see cref="InvalidOperationException"/>. There is no known operator that composes cleanly on
/// top of this wrapper; treat these calls as terminal calls in the query. The two calls can be
/// chained with each other in either order: a later call merges into the marker an earlier one
/// created (see <see cref="Merge"/>).
/// </para>
/// </remarks>
internal sealed class CouchbaseQueryOptionsMarkerExpression(
    Expression inner,
    string? consistentWithParameterName,
    QueryScanConsistency? scanConsistency) : Expression
{
    public Expression Inner { get; } = inner;

    /// <summary>
    /// The generated query-parameter name holding a <c>ConsistentWith</c> call's
    /// <see cref="MutationState"/>, or <see langword="null"/> if that was not called.
    /// </summary>
    public string? ConsistentWithParameterName { get; } = consistentWithParameterName;

    /// <summary>
    /// The per-query scan consistency a <c>WithScanConsistency</c> call set, or
    /// <see langword="null"/> if that was not called (the context-wide setting then applies).
    /// </summary>
    public QueryScanConsistency? ScanConsistency { get; } = scanConsistency;

    public override Type Type => Inner.Type;

    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <summary>
    /// Wraps <paramref name="shaper"/> with the given options. If <paramref name="shaper"/> is already
    /// one of these markers (an earlier <c>ConsistentWith</c>/<c>WithScanConsistency</c> call), the
    /// options are merged into it instead of nesting a second marker, and a value passed here
    /// replaces the earlier one for the same option (the last call wins).
    /// </summary>
    public static CouchbaseQueryOptionsMarkerExpression Merge(
        Expression shaper, string? consistentWithParameterName, QueryScanConsistency? scanConsistency)
        => shaper is CouchbaseQueryOptionsMarkerExpression existing
            ? new CouchbaseQueryOptionsMarkerExpression(
                existing.Inner,
                consistentWithParameterName ?? existing.ConsistentWithParameterName,
                scanConsistency ?? existing.ScanConsistency)
            : new CouchbaseQueryOptionsMarkerExpression(shaper, consistentWithParameterName, scanConsistency);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedInner = visitor.Visit(Inner);
        return visitedInner == Inner
            ? this
            : new CouchbaseQueryOptionsMarkerExpression(visitedInner, ConsistentWithParameterName, ScanConsistency);
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
