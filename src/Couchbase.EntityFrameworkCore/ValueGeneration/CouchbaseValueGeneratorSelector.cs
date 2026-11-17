using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Selects the appropriate value generator for properties in a Couchbase EF Core context.
/// </summary>
/// <remarks>
/// A property that uses a Couchbase sequence gets a temporary value when the entity is added, from
/// the relational base selector. The real value comes from the sequence when the changes are saved
/// (see <c>CouchbaseDatabaseWrapper</c>), so <c>Add</c> never does I/O.
/// </remarks>
public class CouchbaseValueGeneratorSelector : RelationalValueGeneratorSelector
{
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

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseValueGeneratorSelector"/>.
    /// </summary>
    /// <param name="dependencies">The selector's dependencies.</param>
    /// <param name="optionsBuilder">Unused; kept so existing service registrations still resolve.</param>
    public CouchbaseValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies,
        ICouchbaseDbContextOptionsBuilder optionsBuilder)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Selects a value generator for the given property, preferring the GUID string generator when
    /// its annotation is present.
    /// </summary>
    public override bool TrySelect(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
        => TryCreateCouchbaseValueGenerator(property, out valueGenerator)
            || base.TrySelect(property, typeBase, out valueGenerator);

    /// <summary>
    /// Creates a value generator for the given property, preferring the GUID string generator when
    /// its annotation is present.
    /// </summary>
    public override bool TryCreate(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
        => TryCreateCouchbaseValueGenerator(property, out valueGenerator)
            || base.TryCreate(property, typeBase, out valueGenerator);

    /// <summary>
    /// Returns the GUID string generator when the property is annotated for one, otherwise
    /// <c>false</c> so the relational base can select a generator. Sequence properties fall through
    /// to the base, which gives them temporary values.
    /// </summary>
    private static bool TryCreateCouchbaseValueGenerator(IProperty property, out ValueGenerator? valueGenerator)
    {
        var guidStringFormat = property.FindAnnotation(GuidStringFormatAnnotation)?.Value as string;
        if (guidStringFormat != null && property.ClrType == typeof(string))
        {
            valueGenerator = new CouchbaseGuidStringValueGenerator(guidStringFormat);
            return true;
        }

        valueGenerator = null;
        return false;
    }
}
