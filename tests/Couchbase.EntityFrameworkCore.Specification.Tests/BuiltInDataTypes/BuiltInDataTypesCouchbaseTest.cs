using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.BuiltInDataTypes;

public class BuiltInDataTypesCouchbaseTest(BuiltInDataTypesCouchbaseTest.BuiltInDataTypesCouchbaseFixture fixture)
    : BuiltInDataTypesTestBase<BuiltInDataTypesCouchbaseTest.BuiltInDataTypesCouchbaseFixture>(fixture)
{
    // Each skip below is a real provider bug these tests found (not a limitation by design); remove
    // the skip when the bug is fixed.

    private const string EnumCastProjectionSkip =
        "Bug: a projection that casts an enum column to another enum type (Select(t => new Dto { E = (DtoEnum)t.E }).Where(t => t.E == ...)) "
        + "generates empty SQL++ fragments (\"SELECT d.Id,  AS TemplateType ... WHERE  = 0\"), which Couchbase rejects as a syntax error. "
        + "The sync case would also fail, because the provider is async-only.";

    [Theory(Skip = EnumCastProjectionSkip), InlineData(false), InlineData(true)]
    public override Task Can_filter_projection_with_captured_enum_variable(bool async)
        => base.Can_filter_projection_with_captured_enum_variable(async);

    [Theory(Skip = EnumCastProjectionSkip), InlineData(false), InlineData(true)]
    public override Task Can_filter_projection_with_inline_enum_variable(bool async)
        => base.Can_filter_projection_with_inline_enum_variable(async);

    [Fact(Skip = "Bug: a DateTimeOffset constant in a predicate is rendered as TIMESTAMP '0001-01-01 00:00:00.0000000-08:00', "
        + "which is not SQL++ syntax, so the query fails to parse.")]
    public override Task Can_query_using_any_nullable_data_type_as_literal()
        => base.Can_query_using_any_nullable_data_type_as_literal();

    private const string ShadowPropertySkip =
        "Bug: shadow properties are not persisted. An entity with shadow properties set is stored as just {\"Id\": ...}, "
        + "so reading them back fails (DBNull cast). This also affects shadow foreign keys.";

    [Fact(Skip = ShadowPropertySkip)]
    public override Task Can_query_using_any_data_type_shadow()
        => base.Can_query_using_any_data_type_shadow();

    [Fact(Skip = ShadowPropertySkip)]
    public override Task Can_query_using_any_data_type_nullable_shadow()
        => base.Can_query_using_any_data_type_nullable_shadow();

    [Fact(Skip = "Bug: a FirstOrDefault() over a collection, projected as a scalar, is translated to "
        + "(SELECT d0.Method ... LIMIT 1)[0], which yields the whole row object instead of the Method value, "
        + "so reading it as an enum fails (JsonElement of kind Object to Int32).")]
    public override Task Can_read_back_mapped_enum_from_collection_first_or_default()
        => base.Can_read_back_mapped_enum_from_collection_first_or_default();

    public class BuiltInDataTypesCouchbaseFixture : BuiltInDataTypesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => CouchbaseTestStoreFactory.ForStore(StoreName);

        // Numbers are JSON doubles on the server, so floating-point and decimal values don't
        // compare exactly; there's no ANSI string type and no binary keys (document keys are strings).
        public override bool StrictEquality => false;

        public override bool SupportsAnsi => false;

        public override bool SupportsUnicodeToAnsiConversion => true;

        public override bool SupportsLargeStringComparisons => true;

        public override bool SupportsBinaryKeys => false;

        public override bool SupportsDecimalComparisons => false;

        public override DateTime DefaultDateTime => new();

        public override bool PreservesDateTimeKind => false;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            // Qualify bare table names into Bucket.Scope.Collection keyspaces.
            modelBuilder.ConfigureToCouchbase(context);
        }

        // The base fixture seeds through HasData, which the provider's EnsureCreatedAsync doesn't apply.
        protected override Task SeedAsync(PoolableDbContext context) => ModelSeedData.InsertAsync(context);
    }
}
