using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Gives a generated integer primary key a Couchbase sequence, since Couchbase has no identity
/// columns.
/// </summary>
/// <remarks>
/// <para>
/// EF Core marks a single-property integer key as generated on add. A relational database
/// generates such a value with an identity column; Couchbase has none, so without a generator
/// every new entity would be written with the same key. This convention configures the key of
/// each root entity type to use a sequence named <c>&lt;EntityName&gt;Sequence</c>, in the
/// context's scope. Derived types share their root's key, so all types in a hierarchy (TPH or TPC)
/// draw from one sequence and their keys are unique across collections.
/// </para>
/// <para>
/// Values are assigned when the changes are saved, not when the entity is added: <c>Add</c> gives
/// the key a temporary value, and <c>SaveChangesAsync</c> fetches all the values it needs from
/// each sequence in one query. <c>EnsureCreated</c> creates the sequences.
/// </para>
/// <para>
/// It does not apply when the key is configured explicitly: <c>ValueGeneratedNever()</c>,
/// <c>UseSequence(...)</c>, <c>[CouchbaseSequence]</c> or a custom value generator.
/// </para>
/// </remarks>
public class CouchbaseKeySequenceConvention : IModelFinalizingConvention
{
    /// <summary>The suffix of a default sequence's name: <c>BlogSequence</c> for <c>Blog</c>.</summary>
    public const string DefaultSequenceSuffix = "Sequence";

    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.BaseType != null || entityType.IsOwned() || entityType.IsMappedToJson())
            {
                continue;
            }

            if (entityType.FindPrimaryKey() is not { Properties: [var property] }
                || property.ValueGenerated != ValueGenerated.OnAdd
                || property.GetValueGeneratorFactory() != null
                || property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation) != null
                || !CouchbaseSequenceTypes.IsSupported(
                    Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType))
            {
                continue;
            }

            property.Builder.HasAnnotation(
                CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
                entityType.ShortName() + DefaultSequenceSuffix);
        }
    }
}
