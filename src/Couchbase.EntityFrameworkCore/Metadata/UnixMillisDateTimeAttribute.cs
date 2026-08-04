namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Stores the annotated <see cref="DateTime"/> property as Unix epoch milliseconds (a JSON
/// <c>NUMBER</c>) instead of this provider's default ISO-8601 string.
/// </summary>
/// <remarks>
/// Under the hood this configures a <see cref="Storage.Internal.UnixMillisDateTimeConverter"/> via
/// <c>HasConversion</c> — EF Core's own standard value-conversion mechanism, so normal
/// materialization/write paths need no Couchbase-specific handling. Query-side member access
/// (<c>.Year</c>, <c>.Date</c>, <c>Add*</c>) is translated to N1QL's <c>_MILLIS</c> date-function
/// family instead of the <c>_STR</c> family used for the default string representation.
/// <para>
/// <see cref="DateTime.UtcNow"/>/<see cref="DateTime.Now"/>/<see cref="DateTime.Today"/> have no
/// associated property, so they cannot be made millis-aware — comparing a
/// <see cref="UnixMillisDateTimeAttribute"/>-annotated property directly against one of these
/// throws at query-translation time. Capture the value into a local variable before the query
/// instead (<c>var now = DateTime.UtcNow; ... Where(x =&gt; x.MillisProp &gt; now)</c>) — the
/// captured value becomes a parameter, which correctly converts through the same mechanism as any
/// other comparand.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class Event
/// {
///     [UnixMillisDateTime]
///     public DateTime OccurredAt { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public class UnixMillisDateTimeAttribute : Attribute
{
}
