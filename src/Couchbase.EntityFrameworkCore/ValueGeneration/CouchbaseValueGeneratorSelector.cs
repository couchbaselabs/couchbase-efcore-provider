using System.Reflection;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
    /// The annotation key for sequence options (start value, increment, etc.).
    /// </summary>
    public const string SequenceOptionsAnnotation = "Couchbase:SequenceOptions";

    /// <summary>
    /// The annotation key for whether to auto-create the sequence on EnsureCreatedAsync.
    /// Defaults to true if not present.
    /// </summary>
    public const string SequenceAutoCreateAnnotation = "Couchbase:SequenceAutoCreate";

    /// <summary>
    /// The annotation key for GUID string format (e.g., "D", "N", "B", "P").
    /// </summary>
    public const string GuidStringFormatAnnotation = "Couchbase:GuidStringFormat";

    private static readonly MethodInfo CreateSequenceGeneratorMethod =
        typeof(CouchbaseValueGeneratorSelector).GetMethod(
            nameof(CreateSequenceValueGeneratorOfType),
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseValueGeneratorSelector"/>.
    /// </summary>
    public CouchbaseValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies,
        ICouchbaseDbContextOptionsBuilder optionsBuilder)
        : base(dependencies)
    {
        _optionsBuilder = optionsBuilder;
    }

    /// <summary>
    /// Selects a value generator for the given property, preferring a Couchbase sequence or GUID
    /// string generator when the corresponding annotation is present.
    /// </summary>
    public override bool TrySelect(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
        => TryCreateCouchbaseValueGenerator(property, out valueGenerator)
            || base.TrySelect(property, typeBase, out valueGenerator);

    /// <summary>
    /// Creates a value generator for the given property, preferring a Couchbase sequence or GUID
    /// string generator when the corresponding annotation is present.
    /// </summary>
    public override bool TryCreate(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
        => TryCreateCouchbaseValueGenerator(property, out valueGenerator)
            || base.TryCreate(property, typeBase, out valueGenerator);

    /// <summary>
    /// Returns a Couchbase-specific value generator (sequence or GUID string) when the property is
    /// annotated for one, otherwise <c>false</c> so the relational base can select a generator.
    /// </summary>
    private bool TryCreateCouchbaseValueGenerator(IProperty property, out ValueGenerator? valueGenerator)
    {
        // Check for sequence-based generation
        var sequenceName = property.FindAnnotation(SequenceNameAnnotation)?.Value as string;
        if (!string.IsNullOrEmpty(sequenceName))
        {
            valueGenerator = CreateSequenceValueGenerator(property, sequenceName);
            return true;
        }

        // Check for GUID string generation
        var guidStringFormat = property.FindAnnotation(GuidStringFormatAnnotation)?.Value as string;
        if (guidStringFormat != null && property.ClrType == typeof(string))
        {
            valueGenerator = new CouchbaseGuidStringValueGenerator(guidStringFormat);
            return true;
        }

        valueGenerator = null;
        return false;
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
            scope);
    }
}
