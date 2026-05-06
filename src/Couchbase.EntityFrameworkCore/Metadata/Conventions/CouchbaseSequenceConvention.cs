using System.Reflection;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// A convention that processes <see cref="CouchbaseSequenceAttribute"/> on properties
/// and configures them to use Couchbase sequence value generation.
/// </summary>
public class CouchbaseSequenceConvention : PropertyAttributeConventionBase<CouchbaseSequenceAttribute>
{
    public CouchbaseSequenceConvention(ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        CouchbaseSequenceAttribute attribute,
        MemberInfo clrMember,
        IConventionContext context)
    {
        // Set the sequence name annotation
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            attribute.SequenceName);

        // Set the scope override if specified
        if (attribute.Scope != null)
        {
            propertyBuilder.HasAnnotation(
                CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
                attribute.Scope);
        }

        // Create sequence options from attribute properties
        var options = new CouchbaseSequenceOptions
        {
            StartWith = attribute.StartWith,
            IncrementBy = attribute.IncrementBy,
            Cycle = attribute.Cycle
        };

        // Only set options annotation if not using defaults (to reduce annotation noise)
        if (options != CouchbaseSequenceOptions.Default || !attribute.AutoCreate)
        {
            // Store auto-create flag with options by using a wrapper or separate annotation
            propertyBuilder.HasAnnotation(
                CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
                options);
        }

        // Mark the property as ValueGeneratedOnAdd
        propertyBuilder.ValueGenerated(ValueGenerated.OnAdd);
    }
}
