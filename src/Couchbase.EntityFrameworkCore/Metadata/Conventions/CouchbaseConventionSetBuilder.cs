using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

public class CouchbaseConventionSetBuilder : RelationalConventionSetBuilder
{
    public CouchbaseConventionSetBuilder(ProviderConventionSetBuilderDependencies dependencies, RelationalConventionSetBuilderDependencies relationalDependencies) 
        : base(dependencies, relationalDependencies)
    {
    }

    public override ConventionSet CreateConventionSet()
    {
        var conventionSet = base.CreateConventionSet();
        conventionSet.Add(new CouchbaseContextConvention(Dependencies));
        conventionSet.Add(new JsonPropertyNameConvention(Dependencies));
        conventionSet.Add(new JsonPropertyConvention(Dependencies));
        return conventionSet;
    }
}