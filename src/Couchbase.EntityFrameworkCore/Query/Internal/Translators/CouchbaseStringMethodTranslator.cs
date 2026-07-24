using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

public class CouchbaseStringMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo IndexOfMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), new[] { typeof(string) })!;

    private static readonly MethodInfo ReplaceMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.Replace), new[] { typeof(string), typeof(string) })!;

    private static readonly MethodInfo ToLowerMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo ToUpperMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

    private static readonly MethodInfo SubstringMethodInfoWithOneArg
        = typeof(string).GetRuntimeMethod(nameof(string.Substring), new[] { typeof(int) })!;

    private static readonly MethodInfo SubstringMethodInfoWithTwoArgs
        = typeof(string).GetRuntimeMethod(nameof(string.Substring), new[] { typeof(int), typeof(int) })!;

    private static readonly MethodInfo IsNullOrWhiteSpaceMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrWhiteSpace), new[] { typeof(string) })!;

    // Method defined in netcoreapp2.0 only
    private static readonly MethodInfo TrimStartMethodInfoWithoutArgs
        = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;

    private static readonly MethodInfo TrimStartMethodInfoWithCharArg
        = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), new[] { typeof(char) })!;

    private static readonly MethodInfo TrimEndMethodInfoWithoutArgs
        = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;

    private static readonly MethodInfo TrimEndMethodInfoWithCharArg
        = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), new[] { typeof(char) })!;

    private static readonly MethodInfo TrimMethodInfoWithoutArgs
        = typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;

    private static readonly MethodInfo TrimMethodInfoWithCharArg
        = typeof(string).GetRuntimeMethod(nameof(string.Trim), new[] { typeof(char) })!;

    // Method defined in netstandard2.0
    private static readonly MethodInfo TrimStartMethodInfoWithCharArrayArg
        = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), new[] { typeof(char[]) })!;

    private static readonly MethodInfo TrimEndMethodInfoWithCharArrayArg
        = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), new[] { typeof(char[]) })!;

    private static readonly MethodInfo TrimMethodInfoWithCharArrayArg
        = typeof(string).GetRuntimeMethod(nameof(string.Trim), new[] { typeof(char[]) })!;

    private static readonly MethodInfo ContainsMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.Contains), new[] { typeof(string) })!;

    private static readonly MethodInfo StartsWithMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), new[] { typeof(string) })!;

    private static readonly MethodInfo EndsWithMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

    private static readonly MethodInfo PadLeftMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.PadLeft), new[] { typeof(int) })!;

    private static readonly MethodInfo PadRightMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.PadRight), new[] { typeof(int) })!;

    // LIKE escape character used both to build the escaped literal pattern (constant case) and
    // as the ESCAPE clause argument sent to N1QL. Deliberately NOT a backslash: N1QL's own string
    // literal syntax also treats backslash as an escape character, so embedding a literal
    // backslash as a SQL literal would itself need doubling (this provider's string-literal
    // generation doesn't do that) -- confirmed empirically via a live-cluster parsing error.
    // '!' has no special meaning in N1QL string literals or as a LIKE wildcard.
    private const string LikeEscapeChar = "!";

    private static readonly MethodInfo FirstOrDefaultMethodInfoWithoutArgs
        = typeof(Enumerable).GetRuntimeMethods().Single(
            m => m.Name == nameof(Enumerable.FirstOrDefault)
                && m.GetParameters().Length == 1).MakeGenericMethod(typeof(char));

    private static readonly MethodInfo LastOrDefaultMethodInfoWithoutArgs
        = typeof(Enumerable).GetRuntimeMethods().Single(
            m => m.Name == nameof(Enumerable.LastOrDefault)
                && m.GetParameters().Length == 1).MakeGenericMethod(typeof(char));

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    
    public CouchbaseStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }
    
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance != null)
        {
            if (IndexOfMethodInfo.Equals(method))
            {
                var argument = arguments[0];
                var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, argument);

                // POSITION returns the zero-based index of the first match, or -1 if not found --
                // an exact match for string.IndexOf's semantics. CONTAINS returns a boolean and
                // must not be used here.
                return _sqlExpressionFactory.Function(
                        "POSITION",
                        new[]
                        {
                            _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping),
                            _sqlExpressionFactory.ApplyTypeMapping(argument, stringTypeMapping)
                        },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true },
                        method.ReturnType);
            }

            if (ReplaceMethodInfo.Equals(method))
            {
                var firstArgument = arguments[0];
                var secondArgument = arguments[1];
                var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, firstArgument, secondArgument);

                return _sqlExpressionFactory.Function(
                    "replace",
                    new[]
                    {
                        _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping),
                        _sqlExpressionFactory.ApplyTypeMapping(firstArgument, stringTypeMapping),
                        _sqlExpressionFactory.ApplyTypeMapping(secondArgument, stringTypeMapping)
                    },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true, true },
                    method.ReturnType,
                    stringTypeMapping);
            }

            if (ToLowerMethodInfo.Equals(method)
                || ToUpperMethodInfo.Equals(method))
            {
                return _sqlExpressionFactory.Function(
                    ToLowerMethodInfo.Equals(method) ? "lower" : "upper",
                    new[] { instance },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true },
                    method.ReturnType,
                    instance.TypeMapping);
            }

            if (SubstringMethodInfoWithOneArg.Equals(method))
            {
                return _sqlExpressionFactory.Function(
                    "substr",
                    new[] { instance, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1)) },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    method.ReturnType,
                    instance.TypeMapping);
            }

            if (SubstringMethodInfoWithTwoArgs.Equals(method))
            {
                return _sqlExpressionFactory.Function(
                    "substr",
                    new[] { instance, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1)), arguments[1] },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true, true },
                    method.ReturnType,
                    instance.TypeMapping);
            }

            if (TrimStartMethodInfoWithoutArgs.Equals(method)
                || TrimStartMethodInfoWithCharArg.Equals(method)
                || TrimStartMethodInfoWithCharArrayArg.Equals(method))
            {
                return ProcessTrimMethod(instance, arguments, "ltrim");
            }

            if (TrimEndMethodInfoWithoutArgs.Equals(method)
                || TrimEndMethodInfoWithCharArg.Equals(method)
                || TrimEndMethodInfoWithCharArrayArg.Equals(method))
            {
                return ProcessTrimMethod(instance, arguments, "rtrim");
            }

            if (TrimMethodInfoWithoutArgs.Equals(method)
                || TrimMethodInfoWithCharArg.Equals(method)
                || TrimMethodInfoWithCharArrayArg.Equals(method))
            {
                return ProcessTrimMethod(instance, arguments, "trim");
            }

            if (ContainsMethodInfo.Equals(method))
            {
                var pattern = arguments[0];
                var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, pattern);

                instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);
                pattern = _sqlExpressionFactory.ApplyTypeMapping(pattern, stringTypeMapping);

                // Note: we add IS NOT NULL checks here since we don't do null semantics/compensation for comparison (greater-than)
                // CONTAINS (and string.Contains) returns a boolean, not an int -- an incorrect
                // return type here can produce wrong materialization if this expression is ever
                // projected directly (e.g. `.Select(x => x.Name.Contains("a"))`) rather than only
                // consumed inside a Where predicate.
                var containsFunction = _sqlExpressionFactory.Function(
                    "CONTAINS",
                    new[] { instance, pattern },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    typeof(bool));

                return
                    _sqlExpressionFactory.AndAlso(
                        _sqlExpressionFactory.IsNotNull(instance),
                        _sqlExpressionFactory.AndAlso(
                            _sqlExpressionFactory.IsNotNull(pattern),
                            _sqlExpressionFactory.ApplyDefaultTypeMapping(containsFunction)));
            }

            if (StartsWithMethodInfo.Equals(method))
            {
                return TranslateStartsEndsWith(instance, arguments[0], startsWith: true);
            }

            if (EndsWithMethodInfo.Equals(method))
            {
                return TranslateStartsEndsWith(instance, arguments[0], startsWith: false);
            }

            if (PadLeftMethodInfo.Equals(method)
                || PadRightMethodInfo.Equals(method))
            {
                return _sqlExpressionFactory.Function(
                    PadLeftMethodInfo.Equals(method) ? "LPAD" : "RPAD",
                    new[] { instance, _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]) },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    method.ReturnType,
                    instance.TypeMapping);
            }
        }

        if (IsNullOrWhiteSpaceMethodInfo.Equals(method))
        {
            var argument = arguments[0];

            return _sqlExpressionFactory.OrElse(
                _sqlExpressionFactory.IsNull(argument),
                _sqlExpressionFactory.Equal(
                    _sqlExpressionFactory.Function(
                        "trim",
                        new[] { argument },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true },
                        argument.Type,
                        argument.TypeMapping),
                    _sqlExpressionFactory.Constant(string.Empty)));
        }

        if (FirstOrDefaultMethodInfoWithoutArgs.Equals(method))
        {
            var argument = arguments[0];
            return _sqlExpressionFactory.Function(
                "substr",
                new[] { argument, _sqlExpressionFactory.Constant(1), _sqlExpressionFactory.Constant(1) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, true },
                method.ReturnType);
        }

        if (LastOrDefaultMethodInfoWithoutArgs.Equals(method))
        {
            var argument = arguments[0];
            return _sqlExpressionFactory.Function(
                "substr",
                new[]
                {
                    argument,
                    _sqlExpressionFactory.Function(
                        "length",
                        new[] { argument },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true },
                        typeof(int)),
                    _sqlExpressionFactory.Constant(1)
                },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, true },
                method.ReturnType);
        }

        return null;
    }

    /// <summary>
    /// Translates <see cref="string.StartsWith(string)"/>/<see cref="string.EndsWith(string)"/>
    /// to a N1QL <c>LIKE</c> expression. N1QL's <c>LIKE</c> is ANSI-standard (<c>%</c>/<c>_</c>
    /// wildcards, <c>ESCAPE</c> clause), so a literal search value must have any <c>%</c>/<c>_</c>/
    /// escape-char characters escaped -- otherwise e.g. <c>StartsWith("50%")</c> would treat the
    /// literal <c>%</c> as a wildcard. A constant pattern is escaped once at translation time; a
    /// runtime pattern (parameter or column) is escaped at query time via nested <c>REPLACE</c>
    /// calls, mirroring the approach EF Core's own relational providers use for this exact problem.
    /// </summary>
    private SqlExpression? TranslateStartsEndsWith(SqlExpression instance, SqlExpression pattern, bool startsWith)
    {
        var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, pattern);
        instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);
        pattern = _sqlExpressionFactory.ApplyTypeMapping(pattern, stringTypeMapping);

        SqlExpression likePattern;
        if (pattern is SqlConstantExpression { Value: string constantPattern })
        {
            var escaped = EscapeLikePattern(constantPattern);
            likePattern = _sqlExpressionFactory.Constant(
                startsWith ? escaped + "%" : "%" + escaped,
                stringTypeMapping);
        }
        else
        {
            var escapedPattern = _sqlExpressionFactory.Function(
                "REPLACE",
                new[] { pattern, _sqlExpressionFactory.Constant(LikeEscapeChar, stringTypeMapping), _sqlExpressionFactory.Constant(LikeEscapeChar + LikeEscapeChar, stringTypeMapping) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, true },
                typeof(string),
                stringTypeMapping);
            escapedPattern = _sqlExpressionFactory.Function(
                "REPLACE",
                new[] { escapedPattern, _sqlExpressionFactory.Constant("%", stringTypeMapping), _sqlExpressionFactory.Constant(LikeEscapeChar + "%", stringTypeMapping) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, true },
                typeof(string),
                stringTypeMapping);
            escapedPattern = _sqlExpressionFactory.Function(
                "REPLACE",
                new[] { escapedPattern, _sqlExpressionFactory.Constant("_", stringTypeMapping), _sqlExpressionFactory.Constant(LikeEscapeChar + "_", stringTypeMapping) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, true },
                typeof(string),
                stringTypeMapping);

            likePattern = startsWith
                ? _sqlExpressionFactory.Add(escapedPattern, _sqlExpressionFactory.Constant("%", stringTypeMapping))
                : _sqlExpressionFactory.Add(_sqlExpressionFactory.Constant("%", stringTypeMapping), escapedPattern);
        }

        return _sqlExpressionFactory.Like(instance, likePattern, _sqlExpressionFactory.Constant(LikeEscapeChar, stringTypeMapping));
    }

    private static string EscapeLikePattern(string pattern)
        => pattern
            .Replace(LikeEscapeChar, LikeEscapeChar + LikeEscapeChar)
            .Replace("%", LikeEscapeChar + "%")
            .Replace("_", LikeEscapeChar + "_");

    private SqlExpression? ProcessTrimMethod(SqlExpression instance, IReadOnlyList<SqlExpression> arguments, string functionName)
    {
        var typeMapping = instance.TypeMapping;
        if (typeMapping == null)
        {
            return null;
        }

        var sqlArguments = new List<SqlExpression> { instance };
        if (arguments.Count == 1)
        {
            var constantValue = (arguments[0] as SqlConstantExpression)?.Value;
            var charactersToTrim = new List<char>();

            if (constantValue is char singleChar)
            {
                charactersToTrim.Add(singleChar);
            }
            else if (constantValue is char[] charArray)
            {
                charactersToTrim.AddRange(charArray);
            }
            else
            {
                return null;
            }

            if (charactersToTrim.Count > 0)
            {
                sqlArguments.Add(_sqlExpressionFactory.Constant(new string(charactersToTrim.ToArray()), typeMapping));
            }
        }

        return _sqlExpressionFactory.Function(
            functionName,
            sqlArguments,
            nullable: true,
            argumentsPropagateNullability: sqlArguments.Select(_ => true).ToList(),
            typeof(string),
            typeMapping);
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
