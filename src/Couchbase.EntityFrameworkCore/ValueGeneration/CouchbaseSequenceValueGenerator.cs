using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// A value generator that generates sequential values using a Couchbase sequence.
/// </summary>
/// <remarks>
/// This generator executes <c>SELECT NEXT VALUE FOR `bucket`.`scope`.`sequence_name`</c>
/// to obtain the next value from a Couchbase sequence. Sequences must be created
/// in the database before use.
/// </remarks>
public class CouchbaseSequenceValueGenerator<T> : ValueGenerator<T>
    where T : struct
{
    private static readonly HashSet<Type> SupportedTypes = new()
    {
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
        typeof(decimal)
    };

    private readonly string _sequenceName;
    private readonly string _bucket;
    private readonly string _scope;
    private readonly Func<string, CancellationToken, Task<long>> _executeSequenceQuery;

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseSequenceValueGenerator{T}"/>.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <param name="bucket">The bucket containing the sequence.</param>
    /// <param name="scope">The scope containing the sequence.</param>
    /// <param name="executeSequenceQuery">
    /// A delegate that executes the sequence query and returns the next value.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="T"/> is not a supported numeric type.
    /// </exception>
    public CouchbaseSequenceValueGenerator(
        string sequenceName,
        string bucket,
        string scope,
        Func<string, CancellationToken, Task<long>> executeSequenceQuery)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);
        ArgumentException.ThrowIfNullOrEmpty(bucket);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentNullException.ThrowIfNull(executeSequenceQuery);

        if (!SupportedTypes.Contains(typeof(T)))
        {
            throw new InvalidOperationException(
                $"Couchbase sequence value generation is not supported for type '{typeof(T).Name}'. " +
                $"Supported types are: {string.Join(", ", SupportedTypes.Select(t => t.Name))}.");
        }

        _sequenceName = sequenceName;
        _bucket = bucket;
        _scope = scope;
        _executeSequenceQuery = executeSequenceQuery;
    }

    /// <summary>
    /// Gets the SQL++ query used to fetch the next sequence value.
    /// </summary>
    public string SequenceQuery => $"SELECT NEXT VALUE FOR `{_bucket}`.`{_scope}`.`{_sequenceName}`";

    /// <summary>
    /// Gets a value indicating whether values may be temporary (false for sequences).
    /// </summary>
    public override bool GeneratesTemporaryValues => false;

    /// <summary>
    /// Generates the next value for the sequence synchronously.
    /// </summary>
    protected override object NextValue(EntityEntry entry)
    {
        return Next(entry)!;
    }

    /// <summary>
    /// Generates the next value for the sequence.
    /// </summary>
    public override T Next(EntityEntry entry)
    {
        return NextAsync(entry).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Generates the next value for the sequence asynchronously.
    /// </summary>
    public override async ValueTask<T> NextAsync(
        EntityEntry entry,
        CancellationToken cancellationToken = default)
    {
        var query = SequenceQuery;
        var longValue = await _executeSequenceQuery(query, cancellationToken).ConfigureAwait(false);
        return ConvertToTargetType(longValue);
    }

    private static T ConvertToTargetType(long value)
    {
        // Convert the long value to the target type
        // This will throw OverflowException if the value is out of range for the target type
        return (T)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>
    /// Checks if a CLR type is supported for sequence value generation.
    /// </summary>
    public static bool IsTypeSupported(Type type) => SupportedTypes.Contains(type);
}
