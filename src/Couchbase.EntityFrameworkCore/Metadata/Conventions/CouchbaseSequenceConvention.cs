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

        // Mark the property as ValueGeneratedOnAdd
        propertyBuilder.ValueGenerated(ValueGenerated.OnAdd);
    }
}
