namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Overrides the .NET custom <see cref="DateTime"/> format string this provider uses for the
/// annotated property, instead of the DbContext-wide
/// <see cref="Infrastructure.CouchbaseDbContextOptionsBuilder.DateTimeFormat"/> default.
/// </summary>
/// <remarks>
/// N1QL has no native date type — dates are just JSON strings — so nothing stops different
/// properties (or data written by another system) from using a different string convention than
/// the rest of the model. Only applies to the <c>.Date</c> member translator and inline
/// <see cref="DateTime"/> literals for this specific property; the static <c>.Now</c>/<c>.UtcNow</c>/
/// <c>.Today</c> translators have no associated property and always use the context-wide default.
/// See <see cref="Storage.Internal.DotNetToGoDateFormatConverter"/> for the supported token subset.
/// </remarks>
/// <example>
/// <code>
/// public class Order
/// {
///     [DateTimeFormat("yyyy-MM-dd")]
///     public DateTime ShipDate { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public class DateTimeFormatAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified .NET custom <see cref="DateTime"/> format
    /// string.
    /// </summary>
    /// <param name="format">The .NET custom <see cref="DateTime"/> format string.</param>
    public DateTimeFormatAttribute(string format)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        Format = format;
    }

    /// <summary>
    /// Gets the .NET custom <see cref="DateTime"/> format string for this property.
    /// </summary>
    public string Format { get; }
}
