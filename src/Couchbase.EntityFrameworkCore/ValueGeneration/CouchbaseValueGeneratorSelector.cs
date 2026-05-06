using System.Reflection;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Selects the appropriate value generator for properties in a Couchbase EF Core context.
/// </summary>
/// <remarks>
/// This selector creates <see cref="CouchbaseSequenceValueGenerator{T}"/> instances for properties
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

    private static readonly MethodInfo CreateSequenceGeneratorMethod =
        typeof(CouchbaseValueGeneratorSelector).GetMethod(
            nameof(CreateSequenceValueGeneratorOfType),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

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

    private ValueGenerator CreateSequenceValueGenerator(
        IProperty property,
        string sequenceName)
    {
        var clrType = property.ClrType;

        // Handle nullable types by getting the underlying type
        var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        // Validate that the property type is supported
        if (!CouchbaseSequenceValueGenerator<int>.IsTypeSupported(underlyingType))
        {
            throw new InvalidOperationException(
                $"Cannot use Couchbase sequence for property '{property.DeclaringType.ClrType.Name}.{property.Name}' " +
                $"because the property type '{clrType.Name}' is not supported. " +
                $"Supported types are: int, long, short, byte, uint, ulong, ushort, decimal.");
        }

        // Use reflection to create the correct generic type
        var method = CreateSequenceGeneratorMethod.MakeGenericMethod(underlyingType);
        return (ValueGenerator)method.Invoke(this, [property, sequenceName])!;
    }

    private CouchbaseSequenceValueGenerator<T> CreateSequenceValueGeneratorOfType<T>(
        IProperty property,
        string sequenceName)
        where T : struct
    {
        var scopeOverride = property.FindAnnotation(SequenceScopeAnnotation)?.Value as string;
        var scope = scopeOverride ?? _optionsBuilder.Scope;
        var bucket = _optionsBuilder.Bucket;

        return new CouchbaseSequenceValueGenerator<T>(
            sequenceName,
            bucket,
            scope,
            ExecuteSequenceQueryAsync);
    }

    private async Task<long> ExecuteSequenceQueryAsync(string query)
    {
        var opened = await _connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
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
        finally
        {
            if (opened)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
