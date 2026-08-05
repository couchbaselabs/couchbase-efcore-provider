using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Overrides <see cref="GenerateGreatest"/>/<see cref="GenerateLeast"/> to support
/// <c>Math.Max</c>/<c>Math.Min</c> and <c>EF.Functions.Greatest</c>/<c>EF.Functions.Least</c>.
/// </summary>
/// <remarks>
/// EF Core's own core <c>RelationalSqlTranslatingExpressionVisitor</c> intercepts these methods
/// directly (confirmed by reading its source -- <c>case { Method.Name: nameof(Math.Max) or
/// nameof(Math.Min), ... }</c>), calling these two virtual hooks -- which the base class returns
/// <see langword="null"/> from by default, meaning a provider must override them to support these
/// methods at all. This was found the hard way: registering <c>Math.Min</c>/<c>Math.Max</c> in
/// <c>CouchbaseMathMethodTranslator</c> (an <c>IMethodCallTranslator</c>) is dead code for these two
/// specific methods -- the core visitor never reaches method-call translators for them.
/// <para>
/// N1QL has no variadic <c>GREATEST</c>/<c>LEAST</c> function; the equivalent is
/// <c>ARRAY_MAX</c>/<c>ARRAY_MIN</c>, which take a single array argument. Built via
/// <see cref="CouchbaseArrayConstantExpression"/> (an inline array literal) wrapped in a
/// <c>SqlFunctionExpression</c> call to <c>ARRAY_MAX</c>/<c>ARRAY_MIN</c>. This naturally supports
/// any number of arguments (not just two) -- both <c>EF.Functions.Greatest</c>/<c>Least</c>'s own
/// N-ary array-parameter shape and the core visitor's own flattening of nested
/// <c>Math.Max(Math.Max(a, b), c)</c>-style calls into a single N-ary call.
/// </para>
/// </remarks>
public class CouchbaseSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    public CouchbaseSqlTranslatingExpressionVisitor(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
        : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
    }

    public override SqlExpression? GenerateGreatest(IReadOnlyList<SqlExpression> expressions, Type resultType)
        => GenerateArrayAggregate("ARRAY_MAX", expressions, resultType);

    public override SqlExpression? GenerateLeast(IReadOnlyList<SqlExpression> expressions, Type resultType)
        => GenerateArrayAggregate("ARRAY_MIN", expressions, resultType);

    private SqlExpression? GenerateArrayAggregate(string functionName, IReadOnlyList<SqlExpression> expressions, Type resultType)
    {
        // RelationalTypeMappingPostprocessor requires every SqlExpression in the tree -- including
        // each individual element, not just the array literal wrapper -- to end up with a non-null
        // TypeMapping. A bare literal argument (e.g. the "1.0" in Math.Max(x, 1.0)) has none yet at
        // this point in translation, since nothing about our custom array-literal shape lets EF
        // Core's normal contextual inference (e.g. from the other side of a comparison) reach it.
        // Align every element to the first one with a resolved mapping (typically a column) --
        // reasonable since every argument to Math.Max/Min/EF.Functions.Greatest/Least is expected
        // to share the same comparable type anyway.
        var sqlExpressionFactory = Dependencies.SqlExpressionFactory;
        var sharedTypeMapping = expressions.Select(e => e.TypeMapping).FirstOrDefault(m => m != null)
            ?? Dependencies.TypeMappingSource.FindMapping(resultType);
        if (sharedTypeMapping == null)
        {
            // FindMapping legitimately returns null for CLR types with no registered mapping --
            // fail translation cleanly here (matching the base class's own null-means-unsupported
            // convention for these hooks) rather than building a tree with a null TypeMapping that
            // would surface as a much more confusing failure later in RelationalTypeMappingPostprocessor.
            return null;
        }

        var alignedExpressions = expressions
            .Select(e => sqlExpressionFactory.ApplyTypeMapping(e, sharedTypeMapping))
            .ToArray();

        var arrayLiteral = new CouchbaseArrayConstantExpression(
            alignedExpressions, resultType.MakeArrayType(), sharedTypeMapping);

        return Dependencies.SqlExpressionFactory.Function(
            functionName,
            new SqlExpression[] { arrayLiteral },
            nullable: true,
            argumentsPropagateNullability: new[] { true },
            resultType);
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
