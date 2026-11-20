using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// EF Core's relational value generation convention, with integer keys in a TPC hierarchy
/// generated too.
/// </summary>
/// <remarks>
/// The relational convention leaves a TPC integer key not generated, because an identity column
/// generates values per table and TPC needs keys that are unique across the hierarchy's tables.
/// Couchbase generates integer keys from a sequence shared by the whole hierarchy
/// (<see cref="CouchbaseKeySequenceConvention"/>), so they are unique across its collections, as
/// SQL Server does with its sequence strategy.
/// </remarks>
public class CouchbaseValueGenerationConvention : RelationalValueGenerationConvention
{
    /// <summary>Creates the convention.</summary>
    public CouchbaseValueGenerationConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    protected override bool MappingStrategyAllowsValueGeneration(IConventionProperty property, string? mappingStrategy)
        => true;
}
