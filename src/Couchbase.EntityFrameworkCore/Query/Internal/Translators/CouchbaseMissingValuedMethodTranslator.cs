using System.Reflection;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="CouchbaseDbFunctionsExtensions"/>'s <c>IsMissing</c>/<c>IsNotMissing</c>/
/// <c>IsValued</c>/<c>IsNotValued</c> into a marker <see cref="SqlFunctionExpression"/> that
/// <see cref="Query.Internal.CouchbaseQuerySqlGenerator"/> rewrites into N1QL's postfix
/// <c>IS [NOT] MISSING</c>/<c>IS [NOT] VALUED</c> operators.
/// </summary>
/// <remarks>
/// A regular <see cref="SqlFunctionExpression"/> is used purely as a carrier — there is no valid
/// "function-call" rendering for these (N1QL's IS MISSING family is a postfix operator on an
/// operand, not a callable function taking parenthesized arguments), so
/// <see cref="Query.Internal.CouchbaseQuerySqlGenerator"/> recognizes the four internal function
/// names below and substitutes postfix syntax instead — the same "detect a specific builtin name,
/// substitute custom rendering" pattern already used there for <c>COALESCE</c> -&gt;
/// <c>IFMISSINGORNULL</c>. Reusing <see cref="SqlFunctionExpression"/> (rather than a new
/// <see cref="SqlExpression"/> subtype) means the base <c>SqlNullabilityProcessor</c> already knows
/// how to process it generically — no new nullability-processor handling is needed.
/// </remarks>
public class CouchbaseMissingValuedMethodTranslator : IMethodCallTranslator
{
    internal const string IsMissingFunctionName = "COUCHBASE_IS_MISSING";
    internal const string IsNotMissingFunctionName = "COUCHBASE_IS_NOT_MISSING";
    internal const string IsValuedFunctionName = "COUCHBASE_IS_VALUED";
    internal const string IsNotValuedFunctionName = "COUCHBASE_IS_NOT_VALUED";

    private static readonly MethodInfo IsMissingMethodInfo = GetOpenMethod(nameof(CouchbaseDbFunctionsExtensions.IsMissing));
    private static readonly MethodInfo IsNotMissingMethodInfo = GetOpenMethod(nameof(CouchbaseDbFunctionsExtensions.IsNotMissing));
    private static readonly MethodInfo IsValuedMethodInfo = GetOpenMethod(nameof(CouchbaseDbFunctionsExtensions.IsValued));
    private static readonly MethodInfo IsNotValuedMethodInfo = GetOpenMethod(nameof(CouchbaseDbFunctionsExtensions.IsNotValued));

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public CouchbaseMissingValuedMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // IsMissing/etc. are generic (T value) -- like EF Core's own Greatest<T>/Least<T> -- so
        // `method` here is a closed generic method instance and must be compared against its own
        // generic method definition, not via MethodInfo.Equals directly.
        if (!method.IsGenericMethod)
        {
            return null;
        }

        var genericMethodDefinition = method.GetGenericMethodDefinition();
        var functionName = genericMethodDefinition == IsMissingMethodInfo ? IsMissingFunctionName
            : genericMethodDefinition == IsNotMissingMethodInfo ? IsNotMissingFunctionName
            : genericMethodDefinition == IsValuedMethodInfo ? IsValuedFunctionName
            : genericMethodDefinition == IsNotValuedMethodInfo ? IsNotValuedFunctionName
            : null;

        if (functionName is null)
        {
            return null;
        }

        // arguments[0] is the EF.Functions marker itself (translated to a placeholder by EF
        // Core's own visitor); the real operand is arguments[1] -- matches how EF Core's own
        // LikeTranslator reads arguments[1]/[2] for DbFunctionsExtensions.Like's matchExpression/
        // pattern past its own DbFunctions receiver.
        var function = _sqlExpressionFactory.Function(
            functionName,
            new[] { arguments[1] },
            nullable: false,
            argumentsPropagateNullability: new[] { false },
            typeof(bool));

        return _sqlExpressionFactory.ApplyDefaultTypeMapping(function);
    }

    private static MethodInfo GetOpenMethod(string name)
        => typeof(CouchbaseDbFunctionsExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name);
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
