using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Selects the appropriate value generator for properties in a Couchbase EF Core context.
/// </summary>
/// <remarks>
/// This selector creates <see cref="CouchbaseSequenceValueGenerator"/> instances for properties
/// configured to use Couchbase sequences via <see cref="CouchbasePropertyExtensions.UseSequence"/>.
/// </remarks>
public class CouchbaseValueGeneratorSelector : RelationalValueGeneratorSelector
{
    private readonly IRelationalConnection _connection;
    private readonly ICouchbaseDbContextOptionsBuilder _optionsBuilder;

    /// <summary>
    /// The annotation key used to mark a property as using a Couchbase sequence.
    /// </summary>
    public const string SequenceNameAnnotation = "Couchbase:SequenceName";

    /// <summary>
    /// The annotation key for an optional scope override for the sequence.
    /// </summary>
    public const string SequenceScopeAnnotation = "Couchbase:SequenceScope";

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseValueGeneratorSelector"/>.
    /// </summary>
    public CouchbaseValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies,
        IRelationalConnection connection,
        ICouchbaseDbContextOptionsBuilder optionsBuilder)
        : base(dependencies)
    {
        _connection = connection;
        _optionsBuilder = optionsBuilder;
    }

    /// <summary>
    /// Creates a value generator for the given property.
    /// </summary>
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
    {
        var sequenceName = property.FindAnnotation(SequenceNameAnnotation)?.Value as string;

        if (!string.IsNullOrEmpty(sequenceName))
        {
            return CreateSequenceValueGenerator(property, sequenceName);
        }

        return base.Create(property, typeBase);
    }

    private CouchbaseSequenceValueGenerator CreateSequenceValueGenerator(
        IProperty property,
        string sequenceName)
    {
        // Determine the scope for the sequence
        var scopeOverride = property.FindAnnotation(SequenceScopeAnnotation)?.Value as string;
        var scope = scopeOverride ?? _optionsBuilder.Scope;
        var bucket = _optionsBuilder.Bucket;

        var keyspace = $"{bucket}.{scope}";

        return new CouchbaseSequenceValueGenerator(
            sequenceName,
            keyspace,
            ExecuteSequenceQueryAsync);
    }

    private async Task<long> ExecuteSequenceQueryAsync(string query)
    {
        await _connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        var dbConnection = _connection.DbConnection;
        using var command = dbConnection.CreateCommand();
        command.CommandText = query;

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);

        return result switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            decimal dec => (long)dec,
            _ => throw new InvalidOperationException(
                $"Unexpected sequence value type: {result?.GetType().Name ?? "null"}")
        };
    }
}
