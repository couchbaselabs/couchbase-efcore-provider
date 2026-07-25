using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

public class CouchbaseMathMethodTranslator : IMethodCallTranslator
{
    private static readonly IReadOnlyDictionary<MethodInfo, string> SingleArgFunctionMappings = new Dictionary<MethodInfo, string>
    {
        { GetMathMethod(nameof(Math.Abs), typeof(double)), "ABS" },
        { GetMathMethod(nameof(Math.Abs), typeof(decimal)), "ABS" },
        { GetMathMethod(nameof(Math.Abs), typeof(int)), "ABS" },
        { GetMathMethod(nameof(Math.Abs), typeof(long)), "ABS" },
        { GetMathMethod(nameof(Math.Abs), typeof(float)), "ABS" },
        { GetMathMethod(nameof(Math.Ceiling), typeof(double)), "CEIL" },
        { GetMathMethod(nameof(Math.Ceiling), typeof(decimal)), "CEIL" },
        { GetMathMethod(nameof(Math.Floor), typeof(double)), "FLOOR" },
        { GetMathMethod(nameof(Math.Floor), typeof(decimal)), "FLOOR" },
        { GetMathMethod(nameof(Math.Round), typeof(double)), "ROUND" },
        { GetMathMethod(nameof(Math.Round), typeof(decimal)), "ROUND" },
        { GetMathMethod(nameof(Math.Truncate), typeof(double)), "TRUNC" },
        { GetMathMethod(nameof(Math.Truncate), typeof(decimal)), "TRUNC" },
        { GetMathMethod(nameof(Math.Sqrt), typeof(double)), "SQRT" },
        { GetMathMethod(nameof(Math.Sign), typeof(double)), "SIGN" },
        { GetMathMethod(nameof(Math.Sign), typeof(decimal)), "SIGN" },
        { GetMathMethod(nameof(Math.Sign), typeof(int)), "SIGN" },
        { GetMathMethod(nameof(Math.Sign), typeof(long)), "SIGN" },
        { GetMathMethod(nameof(Math.Sign), typeof(float)), "SIGN" },
        { GetMathMethod(nameof(Math.Log), typeof(double)), "LN" },
        { GetMathMethod(nameof(Math.Log10), typeof(double)), "LOG" }, // N1QL's LOG is fixed base-10.
        { GetMathMethod(nameof(Math.Exp), typeof(double)), "EXP" },
    };

    private static readonly IReadOnlyDictionary<MethodInfo, string> RoundWithDigitsFunctionMappings = new Dictionary<MethodInfo, string>
    {
        { GetMathMethod(nameof(Math.Round), typeof(double), typeof(int)), "ROUND" },
        { GetMathMethod(nameof(Math.Round), typeof(decimal), typeof(int)), "ROUND" },
    };

    private static readonly MethodInfo PowMethodInfo = GetMathMethod(nameof(Math.Pow), typeof(double), typeof(double));

    private static readonly MethodInfo LogWithNewBaseMethodInfo = GetMathMethod(nameof(Math.Log), typeof(double), typeof(double));

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public CouchbaseMathMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (SingleArgFunctionMappings.TryGetValue(method, out var functionName))
        {
            return _sqlExpressionFactory.Function(
                functionName,
                new[] { arguments[0] },
                nullable: true,
                argumentsPropagateNullability: new[] { true },
                method.ReturnType);
        }

        if (RoundWithDigitsFunctionMappings.TryGetValue(method, out var roundFunctionName))
        {
            return _sqlExpressionFactory.Function(
                roundFunctionName,
                new[] { arguments[0], arguments[1] },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true },
                method.ReturnType);
        }

        if (PowMethodInfo.Equals(method))
        {
            return _sqlExpressionFactory.Function(
                "POWER",
                new[] { arguments[0], arguments[1] },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true },
                method.ReturnType);
        }

        if (LogWithNewBaseMethodInfo.Equals(method))
        {
            // N1QL has no two-argument LOG; built via the standard change-of-base identity:
            // log_b(x) = ln(x) / ln(b).
            var lnOfValue = _sqlExpressionFactory.Function(
                "LN",
                new[] { arguments[0] },
                nullable: true,
                argumentsPropagateNullability: new[] { true },
                method.ReturnType);
            var lnOfNewBase = _sqlExpressionFactory.Function(
                "LN",
                new[] { arguments[1] },
                nullable: true,
                argumentsPropagateNullability: new[] { true },
                method.ReturnType);

            return _sqlExpressionFactory.Divide(lnOfValue, lnOfNewBase);
        }

        return null;
    }

    private static MethodInfo GetMathMethod(string name, params Type[] parameterTypes)
        => typeof(Math).GetRuntimeMethod(name, parameterTypes)!;
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
