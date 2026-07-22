using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="DateTime"/> member access to SQL++. This provider's <see cref="DateTime"/>
/// type mapping is <c>STRING</c> (ISO-8601), and the stored/parameter format was confirmed
/// empirically (CBEF-23 step-0 spike, <c>DateTimeFormatSpikeTests</c>) to be millisecond-precision
/// with a literal <c>Z</c> suffix for UTC values, e.g. <c>2026-03-14T09:26:53.123Z</c> --
/// <see cref="Fmt"/> reproduces that format for every N1QL date function that accepts an explicit
/// format string, so results stay comparable against already-stored data.
/// <para>
/// Non-obvious gotcha, confirmed against a live cluster: the fractional-seconds group must use
/// Go's <c>.999</c> trimming convention, not a fixed-width <c>.000</c>. .NET's serializer omits
/// the fractional group entirely (and the decimal point with it) when milliseconds are exactly
/// zero -- e.g. midnight serializes as <c>2026-03-14T00:00:00Z</c>, with no <c>.000</c> at all.
/// A fixed-width format produces a string that never matches such a parameter.
/// </para>
/// <para>
/// Second gotcha: the offset directive must be <c>Z07:00</c>, not a literal <c>Z</c>. A literal
/// <c>Z</c> is only correct for UTC values (<c>NOW_UTC</c>, <c>DATE_PART_STR</c>/<c>DATE_TRUNC_STR</c>
/// on the UTC-stored data this provider writes) -- <c>NOW_LOCAL</c> (<see cref="DateTime.Now"/>)
/// returns a value in the query service's local timezone, which is not UTC in general, so forcing
/// a trailing <c>Z</c> onto it would produce a string that both lies about the offset and won't
/// match how a <see cref="DateTimeKind.Local"/> value actually serializes (typically a real
/// <c>+hh:mm</c>/<c>-hh:mm</c> offset). <c>Z07:00</c> renders <c>Z</c> when the offset is zero and
/// the real offset otherwise, so the same format string is correct for both cases.
/// </para>
/// </summary>
public class CouchbaseDateTimeMemberTranslator : IMemberTranslator
{
    // N1QL's Go-reference-time format style; .999 mirrors .NET's variable-width fractional
    // seconds (trimmed, including the dot, when all-zero) rather than always padding to 3 digits.
    // Z07:00 emits Z for a zero offset (UTC) and a real +hh:mm/-hh:mm offset otherwise, rather
    // than forcing an incorrect literal Z onto non-UTC values (see NOW_LOCAL usage below).
    private const string Fmt = "2006-01-02T15:04:05.999Z07:00";

    private static readonly IReadOnlyDictionary<MemberInfo, string> DatePartMappings = new Dictionary<MemberInfo, string>
    {
        { GetDateTimeProperty(nameof(DateTime.Year)), "year" },
        { GetDateTimeProperty(nameof(DateTime.Month)), "month" },
        { GetDateTimeProperty(nameof(DateTime.Day)), "day" },
        { GetDateTimeProperty(nameof(DateTime.Hour)), "hour" },
        { GetDateTimeProperty(nameof(DateTime.Minute)), "minute" },
        { GetDateTimeProperty(nameof(DateTime.Second)), "second" },
        { GetDateTimeProperty(nameof(DateTime.Millisecond)), "millisecond" },
        { GetDateTimeProperty(nameof(DateTime.DayOfWeek)), "day_of_week" },
        { GetDateTimeProperty(nameof(DateTime.DayOfYear)), "day_of_year" },
    };

    private static readonly MemberInfo DateMemberInfo = GetDateTimeProperty(nameof(DateTime.Date));
    private static readonly MemberInfo NowMemberInfo = GetDateTimeProperty(nameof(DateTime.Now));
    private static readonly MemberInfo UtcNowMemberInfo = GetDateTimeProperty(nameof(DateTime.UtcNow));
    private static readonly MemberInfo TodayMemberInfo = GetDateTimeProperty(nameof(DateTime.Today));

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public CouchbaseDateTimeMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance != null && DatePartMappings.TryGetValue(member, out var part))
        {
            return _sqlExpressionFactory.Function(
                "DATE_PART_STR",
                new[] { instance, _sqlExpressionFactory.Constant(part) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true },
                returnType);
        }

        if (instance != null && DateMemberInfo.Equals(member))
        {
            return _sqlExpressionFactory.Function(
                "DATE_TRUNC_STR",
                new[] { instance, _sqlExpressionFactory.Constant("day"), _sqlExpressionFactory.Constant(Fmt) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, true, false },
                returnType,
                instance.TypeMapping);
        }

        if (instance == null && NowMemberInfo.Equals(member))
        {
            return _sqlExpressionFactory.Function(
                "NOW_LOCAL",
                new[] { _sqlExpressionFactory.Constant(Fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                returnType);
        }

        if (instance == null && UtcNowMemberInfo.Equals(member))
        {
            return _sqlExpressionFactory.Function(
                "NOW_UTC",
                new[] { _sqlExpressionFactory.Constant(Fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                returnType);
        }

        if (instance == null && TodayMemberInfo.Equals(member))
        {
            var nowUtc = _sqlExpressionFactory.Function(
                "NOW_UTC",
                new[] { _sqlExpressionFactory.Constant(Fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                returnType);

            return _sqlExpressionFactory.Function(
                "DATE_TRUNC_STR",
                new[] { nowUtc, _sqlExpressionFactory.Constant("day"), _sqlExpressionFactory.Constant(Fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false, false, false },
                returnType);
        }

        return null;
    }

    private static MemberInfo GetDateTimeProperty(string name)
        => typeof(DateTime).GetProperty(name)!;
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
