using System.Text;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Translates a bounded, ISO-8601-relevant subset of .NET custom <see cref="DateTime"/> format
/// tokens into the equivalent Go reference-time layout tokens N1QL's date functions
/// (<c>DATE_TRUNC_STR</c>, <c>NOW_LOCAL</c>, <c>NOW_UTC</c>) expect for their <c>fmt</c> argument.
/// </summary>
/// <remarks>
/// This is deliberately not a general-purpose .NET-format-string engine: it recognizes exactly the
/// tokens needed to express ISO-8601-family date/timestamp conventions (the ones Couchbase's own
/// N1QL documentation recommends) and throws a clear <see cref="ArgumentException"/> naming the
/// unsupported token for anything else, rather than silently producing a wrong or nonsensical Go
/// layout string that would only surface as a confusing SQL++ error (or worse, a wrong result)
/// later at query time.
/// <para>
/// It does honor .NET's two general-purpose escaping mechanisms -- a <c>'...'</c>/<c>"..."</c>
/// quoted literal string and a <c>\x</c> backslash escape -- since without them a very common way
/// to write this exact ISO-8601 pattern, <c>"yyyy-MM-dd'T'HH:mm:ss"</c>, would have its quoted
/// <c>T</c> passed through as literal quote characters plus a T, producing a Go layout that never
/// matches what <see cref="DateTime.ToString(string)"/> actually emits for that same format
/// string (which strips the quotes and emits just <c>T</c>).
/// </para>
/// </remarks>
public static class DotNetToGoDateFormatConverter
{
    // Case-sensitive: .NET's 'M' (month) and 'm' (minute) tokens are distinct, matching how the
    // character-run scan below treats them as separate runs.
    private static readonly IReadOnlyDictionary<(char Character, int Length), string> ExactTokenMappings =
        new Dictionary<(char, int), string>
        {
            { ('y', 4), "2006" },
            { ('M', 2), "01" },
            { ('d', 2), "02" },
            { ('H', 2), "15" },
            { ('m', 2), "04" },
            { ('s', 2), "05" },
            { ('K', 1), "Z07:00" },
        };

    /// <summary>
    /// Converts a .NET custom <see cref="DateTime"/> format string to the equivalent Go
    /// reference-time layout string.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The format string contains a token this converter does not recognize, an unterminated
    /// literal string section, or a trailing escape character.
    /// </exception>
    public static string Convert(string dotNetFormat)
    {
        var result = new StringBuilder(dotNetFormat.Length);
        var i = 0;

        while (i < dotNetFormat.Length)
        {
            var c = dotNetFormat[i];

            // .NET custom format strings support two escaping mechanisms, both of which must be
            // honored BEFORE any token interpretation -- otherwise, e.g., "yyyy-MM-dd'T'HH:mm:ss"
            // (a very common way to write this exact ISO-8601 pattern) would have its quoted 'T'
            // passed through as two literal quote characters plus a T, producing a Go layout that
            // never matches what .NET's own DateTime.ToString actually emits (which strips the
            // quotes and emits just "T"): (1) a literal string delimited by matching single or
            // double quotes, whose contents are copied verbatim with no token interpretation, and
            // (2) a backslash escaping exactly the next character, usable both inside and outside
            // a literal string section.
            if (c is '\'' or '"')
            {
                i = AppendLiteralSection(dotNetFormat, i, c, result);
                continue;
            }

            if (c == '\\')
            {
                if (i + 1 >= dotNetFormat.Length)
                {
                    throw new ArgumentException(
                        $"Format string \"{dotNetFormat}\" ends with a trailing escape character ('\\') with no character to escape.",
                        nameof(dotNetFormat));
                }

                result.Append(dotNetFormat[i + 1]);
                i += 2;
                continue;
            }

            // Uppercase 'T' -- the ISO-8601 date/time separator -- is not a reserved .NET custom
            // format specifier (only lowercase 't'/'tt', the AM/PM designator, are reserved), so
            // .NET itself passes it through literally without requiring it to be escaped. Treat it
            // the same way here rather than mistaking it for an attempted, unsupported specifier.
            if (!char.IsLetter(c) || c == 'T')
            {
                result.Append(c);
                i++;
                continue;
            }

            var runStart = i;
            while (i < dotNetFormat.Length && dotNetFormat[i] == c)
            {
                i++;
            }

            var runLength = i - runStart;

            if (c is 'f' or 'F')
            {
                if (runLength is < 1 or > 7)
                {
                    throw UnsupportedToken(dotNetFormat, c, runLength);
                }

                // Lowercase 'f' is .NET's fixed-width (zero-padded) fractional-seconds specifier;
                // Go's matching directive is a run of '0'. Uppercase 'F' trims trailing zeros
                // (and the decimal point itself if the whole fraction is zero); Go's matching
                // directive is a run of '9'.
                result.Append(c == 'f' ? '0' : '9', runLength);
                continue;
            }

            if (ExactTokenMappings.TryGetValue((c, runLength), out var goToken))
            {
                result.Append(goToken);
                continue;
            }

            throw UnsupportedToken(dotNetFormat, c, runLength);
        }

        return result.ToString();
    }

    /// <summary>
    /// Appends the contents of a literal string section (delimited by matching single or double
    /// quotes, starting at <paramref name="delimiterIndex"/>) to <paramref name="result"/>
    /// verbatim -- no token interpretation inside the section -- honoring backslash escapes the
    /// same way <see cref="Convert"/> does outside a literal section. Returns the index just past
    /// the closing delimiter.
    /// </summary>
    private static int AppendLiteralSection(string dotNetFormat, int delimiterIndex, char delimiter, StringBuilder result)
    {
        var i = delimiterIndex + 1;

        while (true)
        {
            if (i >= dotNetFormat.Length)
            {
                throw new ArgumentException(
                    $"Format string \"{dotNetFormat}\" has an unterminated literal string starting at "
                    + $"position {delimiterIndex} (missing closing '{delimiter}').",
                    nameof(dotNetFormat));
            }

            var c = dotNetFormat[i];

            if (c == '\\')
            {
                if (i + 1 >= dotNetFormat.Length)
                {
                    throw new ArgumentException(
                        $"Format string \"{dotNetFormat}\" ends with a trailing escape character ('\\') with no character to escape.",
                        nameof(dotNetFormat));
                }

                result.Append(dotNetFormat[i + 1]);
                i += 2;
                continue;
            }

            if (c == delimiter)
            {
                return i + 1;
            }

            result.Append(c);
            i++;
        }
    }

    private static ArgumentException UnsupportedToken(string dotNetFormat, char character, int runLength)
        => new(
            $"Unsupported DateTime format token '{new string(character, runLength)}' in \"{dotNetFormat}\". "
            + "Supported tokens: yyyy, MM, dd, HH, mm, ss, f/F (repeated 1-7 times), K, the literal 'T', "
            + "non-letter separator characters (e.g. '-', ':', '.', space), a quoted literal string "
            + "('...' or \"...\"), or a backslash-escaped character (\\x) for any other literal text.",
            nameof(dotNetFormat));
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
