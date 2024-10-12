using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Newtonsoft.Json;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

public class JsonPropertyConvention: PropertyAttributeConventionBase<JsonPropertyAttribute>
{
    public JsonPropertyConvention(ProviderConventionSetBuilderDependencies dependencies) : base(dependencies)
    {
    }

    protected override void ProcessPropertyAdded(IConventionPropertyBuilder propertyBuilder, JsonPropertyAttribute attribute,
        MemberInfo clrMember, IConventionContext context)
    {
        propertyBuilder.HasColumnName(attribute.PropertyName);
    }
}