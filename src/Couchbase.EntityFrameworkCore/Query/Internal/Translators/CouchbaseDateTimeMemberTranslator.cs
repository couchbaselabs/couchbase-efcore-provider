using System.Reflection;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="DateTime"/> member access to SQL++. This provider's <see cref="DateTime"/>
/// type mapping is <c>STRING</c> (ISO-8601). The format used for every N1QL date function that
/// accepts an explicit format string is <see cref="ICouchbaseDbContextOptionsBuilder.GoDateTimeFormat"/>
/// -- the Go reference-time layout equivalent of the user-configurable
/// <see cref="ICouchbaseDbContextOptionsBuilder.DateTimeFormat"/> -- so results stay comparable
/// against however the user's <see cref="DateTime"/> data is actually stored, rather than assuming
/// one hardcoded format (N1QL has no native date type; dates are just JSON strings, so nothing
/// stops a user from storing a different convention). The default format
/// (<c>"yyyy-MM-ddTHH:mm:ss.FFFK"</c>) was confirmed empirically (CBEF-23 step-0 spike,
/// <c>DateTimeFormatSpikeTests</c>) to match this provider's own default <see cref="DateTime"/>
/// serialization -- millisecond-precision with a literal <c>Z</c> suffix for UTC values, e.g.
/// <c>2026-03-14T09:26:53.123Z</c>.
/// <para>
/// Two non-obvious gotchas that motivated the default format's exact token choices, both confirmed
/// against a live cluster (see <see cref="DotNetToGoDateFormatConverter"/> for the token mapping):
/// (1) the fractional-seconds group must use .NET's <c>F</c> (trimmed) specifier / Go's <c>.999</c>
/// convention, not a fixed-width <c>f</c>/<c>.000</c> -- .NET's serializer omits the fractional
/// group entirely (and the decimal point with it) when milliseconds are exactly zero, e.g. midnight
/// serializes as <c>2026-03-14T00:00:00Z</c> with no <c>.000</c> at all, so a fixed-width format
/// would never match such a value. (2) the offset directive must be .NET's <c>K</c> / Go's
/// <c>Z07:00</c>, not a literal <c>Z</c> -- a literal <c>Z</c> is only correct for UTC values
/// (<c>NOW_UTC</c>, <c>DATE_PART_STR</c>/<c>DATE_TRUNC_STR</c> on UTC-stored data) but
/// <c>NOW_LOCAL</c> (<see cref="DateTime.Now"/>) returns a value in the query service's local
/// timezone, which is not UTC in general.
/// </para>
/// <para>
/// The <c>.Date</c> branch prefers a per-property override
/// (<see cref="Metadata.DateTimeFormatAttribute"/>/<c>HasDateTimeFormat</c>), read directly off
/// the instance's own resolved <see cref="CouchbaseDateTimeTypeMapping"/> with no DI wiring
/// needed, falling back to the context-wide default. The static <c>.Now</c>/<c>.UtcNow</c>/
/// <c>.Today</c> branches have no associated property/instance and always use the context-wide
/// default -- there is no per-property signal available for them.
/// </para>
/// <para>
/// If <paramref name="instance"/>'s resolved type mapping carries a
/// <see cref="Storage.Internal.UnixMillisDateTimeConverter"/> (i.e. the property is
/// <see cref="Metadata.UnixMillisDateTimeAttribute"/>-annotated), the date-part and <c>.Date</c>
/// members instead translate to N1QL's <c>_MILLIS</c> date-function family
/// (<c>DATE_PART_MILLIS</c>/<c>DATE_TRUNC_MILLIS</c>), which operates on/returns milliseconds
/// directly -- no format string involved, unlike the <c>_STR</c> family. This check is confirmed
/// necessary empirically: EF Core's own binary-comparison type inference does NOT propagate a
/// converted property's type mapping onto anything -- each side of a comparison/member access is
/// translated independently, so without this branch a millis-mapped property's <c>.Year</c>/
/// <c>.Date</c> would silently emit a <c>_STR</c>-family call against a <c>NUMBER</c> column.
/// </para>
/// </summary>
public class CouchbaseDateTimeMemberTranslator : IMemberTranslator
{
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
    private readonly string _fmt;

    public CouchbaseDateTimeMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        ICouchbaseDbContextOptionsBuilder optionsBuilder)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _fmt = optionsBuilder.GoDateTimeFormat;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance != null && DatePartMappings.TryGetValue(member, out var part))
        {
            if (UnixMillisDateTimeConverter.IsUnixMillis(instance.TypeMapping))
            {
                return _sqlExpressionFactory.Function(
                    "DATE_PART_MILLIS",
                    new[] { instance, _sqlExpressionFactory.Constant(part) },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, false },
                    returnType);
            }

            return _sqlExpressionFactory.Function(
                "DATE_PART_STR",
                new[] { instance, _sqlExpressionFactory.Constant(part) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, false },
                returnType);
        }

        if (instance != null && DateMemberInfo.Equals(member))
        {
            if (UnixMillisDateTimeConverter.IsUnixMillis(instance.TypeMapping))
            {
                return _sqlExpressionFactory.Function(
                    "DATE_TRUNC_MILLIS",
                    new[] { instance, _sqlExpressionFactory.Constant("day") },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, false },
                    returnType,
                    instance.TypeMapping);
            }

            // Prefer the format baked into the instance's own resolved type mapping -- this is
            // how a per-property [DateTimeFormat]/HasDateTimeFormat override (a distinct
            // CouchbaseDateTimeTypeMapping instance per property) takes effect, with zero DI
            // wiring needed. Falls back to the context-wide default for anything else (e.g. a
            // parameter or a column whose mapping didn't resolve to CouchbaseDateTimeTypeMapping).
            var fmt = (instance.TypeMapping as CouchbaseDateTimeTypeMapping)?.GoDateTimeFormat ?? _fmt;

            return _sqlExpressionFactory.Function(
                "DATE_TRUNC_STR",
                new[] { instance, _sqlExpressionFactory.Constant("day"), _sqlExpressionFactory.Constant(fmt) },
                nullable: true,
                argumentsPropagateNullability: new[] { true, false, false },
                returnType,
                instance.TypeMapping);
        }

        if (instance == null && NowMemberInfo.Equals(member))
        {
            return _sqlExpressionFactory.Function(
                "NOW_LOCAL",
                new[] { _sqlExpressionFactory.Constant(_fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                returnType);
        }

        if (instance == null && UtcNowMemberInfo.Equals(member))
        {
            return _sqlExpressionFactory.Function(
                "NOW_UTC",
                new[] { _sqlExpressionFactory.Constant(_fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                returnType);
        }

        if (instance == null && TodayMemberInfo.Equals(member))
        {
            var nowUtc = _sqlExpressionFactory.Function(
                "NOW_UTC",
                new[] { _sqlExpressionFactory.Constant(_fmt) },
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                returnType);

            return _sqlExpressionFactory.Function(
                "DATE_TRUNC_STR",
                new[] { nowUtc, _sqlExpressionFactory.Constant("day"), _sqlExpressionFactory.Constant(_fmt) },
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
