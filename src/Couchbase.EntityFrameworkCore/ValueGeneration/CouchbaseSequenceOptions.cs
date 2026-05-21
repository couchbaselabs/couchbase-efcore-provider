namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Options for configuring a Couchbase sequence.
/// </summary>
/// <remarks>
/// These options map to the SQL++ CREATE SEQUENCE statement options:
/// <code>
/// CREATE SEQUENCE bucket.scope.sequence_name
///     START WITH {StartWith}
///     INCREMENT BY {IncrementBy}
///     [MAXVALUE {MaxValue}]
///     [MINVALUE {MinValue}]
///     [CYCLE | NO CYCLE]
///     [CACHE {CacheSize}]
/// </code>
/// </remarks>
public sealed record CouchbaseSequenceOptions
{
    /// <summary>
    /// The starting value of the sequence. Defaults to 1.
    /// </summary>
    public long StartWith { get; init; } = 1;

    /// <summary>
    /// The increment value for each call to NEXT VALUE FOR. Defaults to 1.
    /// Can be negative for descending sequences.
    /// </summary>
    public long IncrementBy { get; init; } = 1;

    /// <summary>
    /// The maximum value the sequence can generate.
    /// If null, defaults to the maximum value for the sequence's data type.
    /// </summary>
    public long? MaxValue { get; init; }

    /// <summary>
    /// The minimum value the sequence can generate.
    /// If null, defaults to the minimum value for the sequence's data type.
    /// </summary>
    public long? MinValue { get; init; }

    /// <summary>
    /// Whether the sequence should restart from MinValue/MaxValue when the limit is reached.
    /// Defaults to false (NO CYCLE).
    /// </summary>
    public bool Cycle { get; init; } = false;

    /// <summary>
    /// The number of sequence values to cache for performance.
    /// If null, uses the database default (typically 50).
    /// </summary>
    public int? CacheSize { get; init; }

    /// <summary>
    /// Default sequence options (START WITH 1, INCREMENT BY 1, NO CYCLE).
    /// </summary>
    public static CouchbaseSequenceOptions Default { get; } = new();

    /// <summary>
    /// Generates the SQL++ options clause for CREATE SEQUENCE.
    /// </summary>
    /// <returns>
    /// A string containing the SQL++ options (e.g., "START WITH 1 INCREMENT BY 1 NO CYCLE").
    /// </returns>
    public string ToSqlOptionsClause()
    {
        var parts = new List<string>
        {
            $"START WITH {StartWith}",
            $"INCREMENT BY {IncrementBy}"
        };

        if (MaxValue.HasValue)
        {
            parts.Add($"MAXVALUE {MaxValue.Value}");
        }

        if (MinValue.HasValue)
        {
            parts.Add($"MINVALUE {MinValue.Value}");
        }

        parts.Add(Cycle ? "CYCLE" : "NO CYCLE");

        if (CacheSize.HasValue)
        {
            parts.Add($"CACHE {CacheSize.Value}");
        }

        return string.Join(" ", parts);
    }
}
