using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// Test-harness only: EMULATES the proposed fix for "ConfigureToCouchbase runs before EF creates
/// many-to-many join entity types" by qualifying any still-bare table name when the model is finalized.
/// Without it, suites whose models use implicit many-to-many joins (e.g. F1) cannot run at all.
/// </summary>
public class QualifyingCouchbaseConventionSetBuilder(
    ProviderConventionSetBuilderDependencies dependencies,
    RelationalConventionSetBuilderDependencies relationalDependencies,
    string bucket,
    string scope) : CouchbaseConventionSetBuilder(dependencies, relationalDependencies)
{
    public override ConventionSet CreateConventionSet()
    {
        var set = base.CreateConventionSet();
        set.ModelFinalizingConventions.Add(new QualifyBareTableNames(bucket, scope));
        return set;
    }

    private sealed class QualifyBareTableNames(string bucket, string scope) : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                if (entityType.IsOwned()) continue;
                var table = entityType.GetTableName();
                if (string.IsNullOrEmpty(table) || CouchbaseKeyspace.TryParse(table, out _)) continue;
                entityType.SetTableName(new CouchbaseKeyspace(bucket, scope, CouchbaseSpecModel.CollectionName(table)).ToString());
            }
        }
    }
}
