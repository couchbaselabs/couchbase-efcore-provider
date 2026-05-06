namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Specifies that a property should have its value generated using a Couchbase sequence.
/// </summary>
/// <remarks>
/// <para>
/// When applied to a property, the property value will be automatically generated
/// by fetching the next value from the specified Couchbase sequence when a new
/// entity is added to the context.
/// </para>
/// <para>
/// If <see cref="AutoCreate"/> is true (default), the sequence will be created automatically
/// when <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreatedAsync"/> is called.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class Order
/// {
///     [CouchbaseSequence("order_seq")]
///     public long Id { get; set; }
///     
///     public string CustomerName { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public class CouchbaseSequenceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified sequence name.
    /// The sequence will be looked up in the bucket and scope configured on the DbContext.
    /// </summary>
    /// <param name="sequenceName">The name of the sequence.</param>
    public CouchbaseSequenceAttribute(string sequenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);
        SequenceName = sequenceName;
    }

    /// <summary>
    /// Initializes a new instance with the specified scope and sequence name.
    /// The scope specified here overrides the DbContext-level scope for this sequence.
    /// </summary>
    /// <param name="scope">The scope containing the sequence.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    public CouchbaseSequenceAttribute(string scope, string sequenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);
        Scope = scope;
        SequenceName = sequenceName;
    }

    /// <summary>
    /// Gets the name of the sequence.
    /// </summary>
    public string SequenceName { get; }

    /// <summary>
    /// Gets the scope containing the sequence, or <c>null</c> to use the DbContext-level scope.
    /// </summary>
    public string? Scope { get; }

    /// <summary>
    /// Gets or sets the starting value of the sequence. Defaults to 1.
    /// </summary>
    public long StartWith { get; set; } = 1;

    /// <summary>
    /// Gets or sets the increment value for each call to NEXT VALUE FOR. Defaults to 1.
    /// </summary>
    public long IncrementBy { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether the sequence should restart when the limit is reached.
    /// Defaults to false.
    /// </summary>
    public bool Cycle { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to automatically create the sequence on EnsureCreated.
    /// Defaults to true.
    /// </summary>
    public bool AutoCreate { get; set; } = true;
}
