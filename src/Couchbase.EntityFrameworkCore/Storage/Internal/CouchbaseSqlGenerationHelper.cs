using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseSqlGenerationHelper : RelationalSqlGenerationHelper
{
    public CouchbaseSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies) : base(dependencies)
    {
    }

    /// <summary>
    /// Wraps each dot-separated part of <paramref name="identifier"/> in backticks.
    /// Couchbase uses three-part keyspace notation (bucket.scope.collection) stored as a
    /// single dotted string in EF Core's table-name slot, so each segment must be quoted
    /// individually: `default.blogs.personphoto` → `default`.`blogs`.`personphoto`.
    /// </summary>
    public override void DelimitIdentifier(StringBuilder builder, string identifier)
    {
        var span = identifier.AsSpan();
        var dot = span.IndexOf('.');
        if (dot < 0)
        {
            // Fast path: single segment — no allocation needed.
            builder.Append('`');
            EscapeIdentifier(builder, identifier);
            builder.Append('`');
            return;
        }

        var first = true;
        while (true)
        {
            dot = span.IndexOf('.');
            var segment = dot < 0 ? span : span[..dot];
            if (!first) builder.Append('.');
            first = false;
            builder.Append('`');
            EscapeIdentifierSpan(builder, segment);
            builder.Append('`');
            if (dot < 0) break;
            span = span[(dot + 1)..];
        }
    }

    private static void EscapeIdentifierSpan(StringBuilder builder, ReadOnlySpan<char> identifier)
    {
        var start = 0;
        for (var i = 0; i < identifier.Length; i++)
        {
            if (identifier[i] == '`')
            {
                builder.Append(identifier[start..i]);
                builder.Append("``");
                start = i + 1;
            }
        }
        if (start < identifier.Length)
            builder.Append(identifier[start..]);
    }

    public override string DelimitIdentifier(string identifier)
    {
        if (!identifier.Contains('.'))
        {
            return $"`{EscapeIdentifier(identifier)}`";
        }
        var sb = new StringBuilder();
        DelimitIdentifier(sb, identifier);
        return sb.ToString();
    }

    /// <summary>
    /// Escapes backticks in identifiers by doubling them.
    /// This is necessary because Couchbase SQL++ uses backticks as identifier delimiters.
    /// </summary>
    public override string EscapeIdentifier(string identifier)
        => identifier.Replace("`", "``");

    /// <summary>
    /// Escapes backticks in identifiers by doubling them.
    /// This is necessary because Couchbase SQL++ uses backticks as identifier delimiters.
    /// </summary>
    public override void EscapeIdentifier(StringBuilder builder, string identifier)
    {
        var start = 0;
        for (var i = 0; i < identifier.Length; i++)
        {
            if (identifier[i] == '`')
            {
                builder.Append(identifier, start, i - start);
                builder.Append("``");
                start = i + 1;
            }
        }

        if (start < identifier.Length)
        {
            builder.Append(identifier, start, identifier.Length - start);
        }
    }

   public override string GenerateParameterName(string name) =>
       name.StartsWith("$", StringComparison.Ordinal)
       ? name : "$" + name;

   public override void GenerateParameterName(StringBuilder builder, string name)
       => builder.Append('$').Append(name);
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
