using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ModelBuilding;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.ModelBuilding;

// EF's model-building suites, run with the provider's conventions (keyspace mapping, JSON naming,
// sequences, ...). They build models only and need no cluster.

public class CouchbaseModelBuilderFixture : RelationalModelBuilderTest.RelationalModelBuilderFixture
{
    public override TestHelpers TestHelpers => CouchbaseTestHelpers.Instance;
}

public class CouchbaseModelBuilderNonRelationshipTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalNonRelationshipTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderComplexTypeTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalComplexTypeTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderComplexCollectionTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalComplexCollectionTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderInheritanceTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalInheritanceTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderOneToManyTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalOneToManyTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderManyToOneTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalManyToOneTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderOneToOneTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalOneToOneTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderManyToManyTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalManyToManyTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

public class CouchbaseModelBuilderOwnedTypesTest(CouchbaseModelBuilderFixture fixture)
    : RelationalModelBuilderTest.RelationalOwnedTypesTestBase(fixture), IClassFixture<CouchbaseModelBuilderFixture>
{
    protected override ModelBuilderTest.TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
        => new ModelBuilderTest.GenericTestModelBuilder(Fixture, configure!);
}

// The "Model building 101" walkthrough: the documentation's examples, each built through a context.
public class CouchbaseModelBuilding101Test : ModelBuilding101RelationalTestBase
{
    protected override DbContextOptionsBuilder ConfigureContext(DbContextOptionsBuilder optionsBuilder)
        => CouchbaseTestHelpers.Instance.UseProviderOptions(optionsBuilder);
}
