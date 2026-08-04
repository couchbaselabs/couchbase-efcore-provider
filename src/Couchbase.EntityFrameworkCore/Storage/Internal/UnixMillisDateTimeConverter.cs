using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Converts <see cref="DateTime"/> values to/from Unix epoch milliseconds (a JSON <c>NUMBER</c>),
/// for properties configured via <see cref="Metadata.UnixMillisDateTimeAttribute"/>/
/// <c>HasUnixMillisDateTime</c>.
/// </summary>
/// <remarks>
/// Values are treated as UTC on the way in (matching this provider's general UTC-oriented
/// treatment of <see cref="DateTime"/> elsewhere -- see <see cref="CouchbaseDateTimeTypeMapping"/>)
/// and materialize back as <see cref="DateTimeKind.Utc"/>.
/// <para>
/// No custom <see cref="RelationalTypeMapping"/> is needed for this converter: composed with the
/// existing <c>typeof(long)</c> -&gt; <see cref="Microsoft.EntityFrameworkCore.Storage.LongTypeMapping"/>("NUMBER")
/// entry already in <see cref="CouchbaseTypeMappingSource"/>, EF Core's own generic
/// converter-composition machinery (<c>TypeMappingSource.FindMapping(IProperty)</c>) already
/// resolves a correctly-converting mapping with zero provider-specific plumbing -- confirmed
/// empirically via <c>.ToQueryString()</c>: <c>instance.TypeMapping</c> on a converted property is
/// a stock <see cref="Microsoft.EntityFrameworkCore.Storage.LongTypeMapping"/> with
/// <see cref="RelationalTypeMapping.Converter"/> set to this type,
/// <see cref="RelationalTypeMapping.ClrType"/> is <see cref="DateTime"/>, and the provider CLR type
/// is <see cref="long"/>.
/// </para>
/// </remarks>
public class UnixMillisDateTimeConverter : ValueConverter<DateTime, long>
{
    public UnixMillisDateTimeConverter()
        : base(
            d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            l => DateTimeOffset.FromUnixTimeMilliseconds(l).UtcDateTime)
    {
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="mapping"/> is a converted mapping backed
    /// by a <see cref="UnixMillisDateTimeConverter"/> -- i.e. the property/expression it belongs to
    /// is stored as Unix epoch milliseconds rather than this provider's default ISO-8601 string.
    /// </summary>
    public static bool IsUnixMillis(Microsoft.EntityFrameworkCore.Storage.CoreTypeMapping? mapping)
        => mapping is RelationalTypeMapping { Converter: UnixMillisDateTimeConverter };
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
