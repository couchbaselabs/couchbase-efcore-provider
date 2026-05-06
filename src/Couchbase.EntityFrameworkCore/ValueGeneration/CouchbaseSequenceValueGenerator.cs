using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// A value generator that generates sequential values using a Couchbase sequence.
/// </summary>
/// <remarks>
/// This generator executes <c>SELECT NEXT VALUE FOR bucket.scope.sequence_name</c>
/// to obtain the next value from a Couchbase sequence. Sequences must be created
/// in the database before use.
/// </remarks>
public class CouchbaseSequenceValueGenerator : ValueGenerator<long>
{
    private readonly string _sequenceName;
    private readonly string _keyspace;
    private readonly Func<string, Task<long>> _executeSequenceQuery;

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseSequenceValueGenerator"/>.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <param name="keyspace">The keyspace containing the sequence (bucket.scope format).</param>
    /// <param name="executeSequenceQuery">
    /// A delegate that executes the sequence query and returns the next value.
    /// </param>
    public CouchbaseSequenceValueGenerator(
        string sequenceName,
        string keyspace,
        Func<string, Task<long>> executeSequenceQuery)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);
        ArgumentException.ThrowIfNullOrEmpty(keyspace);
        ArgumentNullException.ThrowIfNull(executeSequenceQuery);

        _sequenceName = sequenceName;
        _keyspace = keyspace;
        _executeSequenceQuery = executeSequenceQuery;
    }

    /// <summary>
    /// Gets the SQL++ query used to fetch the next sequence value.
    /// </summary>
    public string SequenceQuery => $"SELECT NEXT VALUE FOR `{_keyspace}`.`{_sequenceName}`";

    /// <summary>
    /// Gets a value indicating whether values may be temporary (false for sequences).
    /// </summary>
    public override bool GeneratesTemporaryValues => false;

    /// <summary>
    /// Generates the next value for the sequence synchronously.
    /// </summary>
    protected override object NextValue(EntityEntry entry)
    {
        return Next(entry);
    }

    /// <summary>
    /// Generates the next value for the sequence.
    /// </summary>
    public override long Next(EntityEntry entry)
    {
        return NextAsync(entry).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Generates the next value for the sequence asynchronously.
    /// </summary>
    public override async ValueTask<long> NextAsync(
        EntityEntry entry,
        CancellationToken cancellationToken = default)
    {
        var query = SequenceQuery;
        return await _executeSequenceQuery(query).ConfigureAwait(false);
    }
}
