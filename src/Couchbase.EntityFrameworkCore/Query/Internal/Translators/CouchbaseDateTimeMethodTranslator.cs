using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="DateTime"/> <c>Add*</c> methods to SQL++. See
/// <see cref="CouchbaseDateTimeMemberTranslator"/>'s doc comment for the observed stored-format
/// background (CBEF-23 step-0 spike). N1QL's <c>DATE_ADD_STR(date_str, n, part)</c> returns the
/// resulting date as a string in the SAME format as its input -- confirmed empirically against a
/// live cluster (adding 1 day to <c>2026-03-14T09:26:53.123Z</c> produced
/// <c>2026-03-15T09:26:53.123Z</c> directly). Couchbase's own documentation describes this
/// function's return value as milliseconds, which does NOT match observed behavior; trust the
/// live result over the docs here. No MILLIS_TO_STR/MILLIS_TO_UTC wrapping is needed.
/// </summary>
public class CouchbaseDateTimeMethodTranslator : IMethodCallTranslator
{
    private static readonly IReadOnlyDictionary<MethodInfo, string> AddMethodMappings = new Dictionary<MethodInfo, string>
    {
        { GetDateTimeMethod(nameof(DateTime.AddYears), typeof(int)), "year" },
        { GetDateTimeMethod(nameof(DateTime.AddMonths), typeof(int)), "month" },
        { GetDateTimeMethod(nameof(DateTime.AddDays), typeof(double)), "day" },
        { GetDateTimeMethod(nameof(DateTime.AddHours), typeof(double)), "hour" },
        { GetDateTimeMethod(nameof(DateTime.AddMinutes), typeof(double)), "minute" },
        { GetDateTimeMethod(nameof(DateTime.AddSeconds), typeof(double)), "second" },
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public CouchbaseDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance != null && AddMethodMappings.TryGetValue(method, out var part))
        {
            return _sqlExpressionFactory.Function(
                "DATE_ADD_STR",
                new[] { instance, arguments[0], _sqlExpressionFactory.Constant(part) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, false },
                method.ReturnType,
                instance.TypeMapping);
        }

        return null;
    }

    private static MethodInfo GetDateTimeMethod(string name, params Type[] parameterTypes)
        => typeof(DateTime).GetRuntimeMethod(name, parameterTypes)!;
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
