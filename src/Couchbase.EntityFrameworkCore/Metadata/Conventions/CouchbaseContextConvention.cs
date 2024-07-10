using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

public class CouchbaseContextConvention : TypeAttributeConventionBase<CouchbaseAttribute>
{
    public CouchbaseContextConvention(ProviderConventionSetBuilderDependencies dependencies) : base(dependencies)
    {
    }

    protected override void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, CouchbaseAttribute attribute,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        entityTypeBuilder.ToTable(attribute.GetContextId());
    }
}