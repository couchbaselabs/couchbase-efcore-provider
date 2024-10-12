using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// When generating the SQL++ if a JsonPropertyNameAttribute exists, the name specified will be used instead.
/// </summary>
public class JsonPropertyNameConvention : PropertyAttributeConventionBase<JsonPropertyNameAttribute>
{
    public JsonPropertyNameConvention(ProviderConventionSetBuilderDependencies dependencies) : base(dependencies)
    {
    }

    protected override void ProcessPropertyAdded(IConventionPropertyBuilder propertyBuilder, JsonPropertyNameAttribute attribute,
        MemberInfo clrMember, IConventionContext context)
    {
        propertyBuilder.HasColumnName(attribute.Name);
    }
}