using System.Linq.Expressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Wraps a <see cref="Microsoft.EntityFrameworkCore.Query.ShapedQueryExpression.ShaperExpression"/>
/// to carry a <c>ConsistentWith</c> call's generated query-parameter name from
/// <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/> (which creates it) to
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
/// <see cref="Extensions.CouchbaseQueryableExtensions.ConsistentWith{TEntity}"/> -- but by throwing,
/// not by silently dropping the hint (confirmed by a regression test, correcting an earlier,
/// untested assumption). In practice this means <c>.ConsistentWith(...)</c> must be the LAST
/// operator applied: even operators like <c>.Where()</c>/<c>.OrderBy()</c>, which only need the
/// <c>QueryExpression</c> conceptually, still bind their lambda parameter against the shaper's
/// shape as part of translation -- and since this wrapper isn't a shape any base-class translation
/// logic recognizes, that binding fails with EF Core's own "could not be translated"
/// <see cref="InvalidOperationException"/>. There is no known operator that composes cleanly on
/// top of this wrapper; treat <c>.ConsistentWith(...)</c> as a terminal call in the query.
/// </para>
/// </remarks>
internal sealed class CouchbaseConsistentWithMarkerExpression(Expression inner, string parameterName) : Expression
{
    public Expression Inner { get; } = inner;

    public string ParameterName { get; } = parameterName;

    public override Type Type => Inner.Type;

    public override ExpressionType NodeType => ExpressionType.Extension;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedInner = visitor.Visit(Inner);
        return visitedInner == Inner ? this : new CouchbaseConsistentWithMarkerExpression(visitedInner, ParameterName);
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
