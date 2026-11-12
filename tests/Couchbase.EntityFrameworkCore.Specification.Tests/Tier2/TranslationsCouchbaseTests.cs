using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.Query.Translations.Operators;
using Microsoft.EntityFrameworkCore.Query.Translations.Temporal;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Tier2;

public class BasicTypesQueryCouchbaseFixture : BasicTypesQueryFixtureBase, ITestSqlLoggerFactory
{
    public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        CouchbaseSpecModel.Apply(modelBuilder, context);
    }
}

public class StringTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : StringTranslationsRelationalTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class MiscellaneousTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : MiscellaneousTranslationsRelationalTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class MathTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : MathTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class ArithmeticTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : ArithmeticOperatorTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class ComparisonTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : ComparisonOperatorTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class LogicalTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : LogicalOperatorTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class BitwiseTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : BitwiseOperatorTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class EnumTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : EnumTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class GuidTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : GuidTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class DateTimeTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : DateTimeTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class DateTimeOffsetTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : DateTimeOffsetTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class DateOnlyTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : DateOnlyTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class TimeOnlyTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : TimeOnlyTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class TimeSpanTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : TimeSpanTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class ByteArrayTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : ByteArrayTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);

public class MiscellaneousOperatorTranslationsCouchbaseTest(BasicTypesQueryCouchbaseFixture fixture)
    : MiscellaneousOperatorTranslationsTestBase<BasicTypesQueryCouchbaseFixture>(fixture);
