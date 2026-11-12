using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;
using Microsoft.EntityFrameworkCore.Query.Associations.OwnedTableSplitting;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.SpecificationTests;

/// <summary>
/// Fails when an EF Core specification test class exists that this project neither extends nor
/// explicitly opts out of via <see cref="IgnoredTestBases"/>. It is the checklist for coverage and
/// flags new upstream test classes after an EF Core upgrade: wire them, or list them here with the
/// reason they do not apply.
/// </summary>
public class CouchbaseComplianceTest : RelationalComplianceTestBase
{
    protected override Assembly TargetAssembly => typeof(CouchbaseComplianceTest).Assembly;

    protected override ICollection<Type> IgnoredTestBases { get; } = new HashSet<Type>
    {
        // TPT inheritance: not supported; the provider rejects it when the model is built.
        typeof(TPTGearsOfWarQueryRelationalTestBase<>),
        typeof(TPTManyToManyQueryRelationalTestBase<>),
        typeof(TPTManyToManyNoTrackingQueryRelationalTestBase<>),
        typeof(TPTRelationshipsQueryTestBase<>),
        typeof(TPTInheritanceQueryTestBase<>),
        typeof(TPTFiltersInheritanceQueryTestBase<>),
        typeof(TPTInheritanceBulkUpdatesTestBase<>),
        typeof(TPTFiltersInheritanceBulkUpdatesTestBase<>),
        typeof(TPTTableSplittingTestBase),

        // Table and entity splitting: embedded data is stored as nested JSON in the owning
        // document, never flattened into the owner's fields or spread over several collections.
        typeof(TableSplittingTestBase),
        typeof(EntitySplittingTestBase),
        typeof(EntitySplittingQueryTestBase),
        typeof(ComplexTableSplittingProjectionRelationalTestBase<>),
        typeof(ComplexTableSplittingBulkUpdateRelationalTestBase<>),
        typeof(ComplexTableSplittingMiscellaneousRelationalTestBase<>),
        typeof(ComplexTableSplittingPrimitiveCollectionRelationalTestBase<>),
        typeof(ComplexTableSplittingStructuralEqualityRelationalTestBase<>),
        typeof(OwnedTableSplittingProjectionRelationalTestBase<>),
        typeof(OwnedTableSplittingMiscellaneousRelationalTestBase<>),
        typeof(OwnedTableSplittingPrimitiveCollectionRelationalTestBase<>),
        typeof(OwnedTableSplittingStructuralEqualityRelationalTestBase<>),

        // Migrations: Couchbase has no schema to migrate; EnsureCreated creates collections and indexes.
        typeof(MigrationsTestBase<>),
        typeof(MigrationsSqlGeneratorTestBase),
        typeof(MigrationsInfrastructureTestBase<>),
        typeof(DesignTimeTestBase<>),

        // Stored procedures: Couchbase has none.
        typeof(StoredProcedureUpdateTestBase),
        typeof(FromSqlSprocQueryTestBase<>),

        // Spatial types: SQL++ has no equivalent of the NetTopologySuite types and functions.
        typeof(SpatialTestBase<>),
        typeof(SpatialQueryTestBase<>),
        typeof(SpatialQueryRelationalTestBase<>),

        // Relational UPDATE/INSERT SQL text: the provider saves through KV and sub-document operations.
        typeof(UpdateSqlGeneratorTestBase),

        // Precompiled queries and compiled models (NativeAOT): not implemented yet.
        typeof(PrecompiledQueryRelationalTestBase),
        typeof(PrecompiledSqlPregenerationQueryRelationalTestBase),
        typeof(AdHocPrecompiledQueryRelationalTestBase),
        typeof(CompiledModelTestBase),
        typeof(CompiledModelRelationalTestBase),

        // Needs a RelationalDbContextOptionsBuilder; the provider's options builder is not one (open decision).
        typeof(LoggingTestBase),
        typeof(LoggingRelationalTestBase<,>),
    };
}
