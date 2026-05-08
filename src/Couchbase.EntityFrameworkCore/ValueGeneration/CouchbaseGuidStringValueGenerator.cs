using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Generates GUID values as strings with a configurable format.
/// </summary>
/// <remarks>
/// <para>
/// This generator creates new GUIDs and formats them as strings according to the specified format.
/// It is useful when you need GUID-based unique identifiers but the property type is <c>string</c>.
/// </para>
/// <para>
/// Supported formats:
/// <list type="bullet">
/// <item><c>"D"</c>: 32 digits with hyphens (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)</item>
/// <item><c>"N"</c>: 32 digits without hyphens</item>
/// <item><c>"B"</c>: 32 digits with hyphens, enclosed in braces</item>
/// <item><c>"P"</c>: 32 digits with hyphens, enclosed in parentheses</item>
/// </list>
/// </para>
/// </remarks>
public class CouchbaseGuidStringValueGenerator : ValueGenerator<string>
{
    private readonly string _format;

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseGuidStringValueGenerator"/>.
    /// </summary>
    /// <param name="format">The GUID string format (default: "D").</param>
    public CouchbaseGuidStringValueGenerator(string format = "D")
    {
        _format = format ?? "D";
        
        // Validate format
        if (_format != "D" && _format != "N" && _format != "B" && _format != "P")
        {
            throw new ArgumentException(
                $"Invalid GUID format '{_format}'. Supported formats are: D, N, B, P.",
                nameof(format));
        }
    }

    /// <summary>
    /// Gets the format string used for generating GUID strings.
    /// </summary>
    public string Format => _format;

    /// <summary>
    /// Gets a value indicating whether the values generated are temporary.
    /// Returns <c>false</c> because GUIDs are permanent values.
    /// </summary>
    public override bool GeneratesTemporaryValues => false;

    /// <summary>
    /// Generates a new GUID string value.
    /// </summary>
    /// <param name="entry">The entity entry for which the value is being generated.</param>
    /// <returns>A new GUID formatted as a string.</returns>
    public override string Next(EntityEntry entry)
    {
        return Guid.NewGuid().ToString(_format);
    }
}
