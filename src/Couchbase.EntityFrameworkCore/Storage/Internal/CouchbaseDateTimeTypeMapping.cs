using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Type mapping for <see cref="DateTime"/> values in Couchbase SQL++.
/// </summary>
/// <remarks>
/// The stock EF Core <see cref="DateTimeTypeMapping"/> generates inline literals as
/// <c>TIMESTAMP 'yyyy-MM-dd HH:mm:ss.fffffff'</c> — a SQL-standard typed-literal syntax no N1QL
/// date-string convention uses, and a fixed format that ignores
/// <see cref="Infrastructure.CouchbaseDbContextOptionsBuilder.DateTimeFormat"/>. This subclass
/// overrides <see cref="SqlLiteralFormatString"/> (the composite format string
/// <see cref="RelationalTypeMapping.GenerateNonNullSqlLiteral"/> feeds through
/// <see cref="string.Format(System.Globalization.CultureInfo,string,object?)"/>) to use the
/// configured format directly as a plain quoted string literal instead.
/// </remarks>
public class CouchbaseDateTimeTypeMapping : DateTimeTypeMapping
{
    private readonly string _dateTimeFormat;
    private readonly string _goDateTimeFormat;

    public CouchbaseDateTimeTypeMapping(string dateTimeFormat)
        : base("STRING", System.Data.DbType.DateTime)
    {
        // Converted eagerly, in the constructor, so an unsupported token throws as soon as this
        // mapping is constructed (model-build time for a per-property override) rather than being
        // deferred to whenever GoDateTimeFormat first happens to be read.
        _goDateTimeFormat = DotNetToGoDateFormatConverter.Convert(dateTimeFormat);
        _dateTimeFormat = dateTimeFormat;
    }

    /// <summary>
    /// The Go reference-time layout equivalent of this mapping's .NET format string, for use in
    /// N1QL date functions (<c>DATE_TRUNC_STR</c>, <c>NOW_UTC</c>, <c>NOW_LOCAL</c>, etc.), which
    /// expect a Go-style layout rather than a .NET custom format string.
    /// </summary>
    public string GoDateTimeFormat => _goDateTimeFormat;

    /// <summary>
    /// Creates a new instance from existing parameters (used for cloning).
    /// </summary>
    protected CouchbaseDateTimeTypeMapping(RelationalTypeMappingParameters parameters, string dateTimeFormat)
        : base(parameters)
    {
        _goDateTimeFormat = DotNetToGoDateFormatConverter.Convert(dateTimeFormat);
        _dateTimeFormat = dateTimeFormat;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new CouchbaseDateTimeTypeMapping(parameters, _dateTimeFormat);

    /// <inheritdoc />
    protected override string SqlLiteralFormatString => $"'{{0:{_dateTimeFormat}}}'";
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
