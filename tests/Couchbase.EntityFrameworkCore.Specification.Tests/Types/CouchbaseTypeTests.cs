using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Types;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Types;

// EF's per-type round trips (save, query by equality, ExecuteUpdate, inside JSON and primitive
// collections), one class per CLR type. Each type gets its own scope.

public abstract class CouchbaseTypeFixture<T> : RelationalTypeFixtureBase<T>
{
    // EF names every type's store "TypeTest"; one scope per type keeps the classes independent.
    protected override string StoreName => typeof(T).Name.Replace("[]", "Array") + "TypeTest";

    // EF isolates each test in a rolled-back transaction shared across contexts. As in the other
    // suites, transactions are no-ops here, so a test's writes are not rolled back.
    public override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
    {
    }

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).AddInterceptors(NoOpTransactionInterceptor.Instance);

    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);
}

public class BoolTypeCouchbaseTest(BoolTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<bool, BoolTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<bool>
    {
        public override bool Value => true;
        public override bool OtherValue => false;
    }
}

public class ByteTypeCouchbaseTest(ByteTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<byte, ByteTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<byte>
    {
        public override byte Value => byte.MinValue;
        public override byte OtherValue => byte.MaxValue;
    }
}

public class ShortTypeCouchbaseTest(ShortTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<short, ShortTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<short>
    {
        public override short Value => short.MinValue;
        public override short OtherValue => short.MaxValue;
    }
}

public class IntTypeCouchbaseTest(IntTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<int, IntTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<int>
    {
        public override int Value => int.MinValue;
        public override int OtherValue => int.MaxValue;
    }
}

public class LongTypeCouchbaseTest(LongTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<long, LongTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<long>
    {
        // The extremes, as on SQL Server: checks that 64-bit integers survive the JSON round trip.
        public override long Value => long.MinValue;
        public override long OtherValue => long.MaxValue;
    }
}

public class DecimalTypeCouchbaseTest(DecimalTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<decimal, DecimalTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<decimal>
    {
        public override decimal Value => 30.5m;
        public override decimal OtherValue => 30m;
    }
}

public class DoubleTypeCouchbaseTest(DoubleTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<double, DoubleTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<double>
    {
        public override double Value => 30.5d;
        public override double OtherValue => 30d;
    }
}

public class FloatTypeCouchbaseTest(FloatTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<float, FloatTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<float>
    {
        public override float Value => 30.5f;
        public override float OtherValue => 30f;
    }
}

public class StringTypeCouchbaseTest(StringTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<string, StringTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<string>
    {
        public override string Value => "foo";
        public override string OtherValue => "bar";
    }
}

public class GuidTypeCouchbaseTest(GuidTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<Guid, GuidTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<Guid>
    {
        public override Guid Value => new("8f7331d6-cde9-44fb-8611-81fff686f280");
        public override Guid OtherValue => new("ae192c36-9004-49b2-b785-8be10d169627");
    }
}

public class DateTimeTypeCouchbaseTest(DateTimeTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<DateTime, DateTimeTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<DateTime>
    {
        public override DateTime Value => new(2020, 1, 5, 12, 30, 45, DateTimeKind.Unspecified);
        public override DateTime OtherValue => new(2022, 5, 3, 0, 0, 0, DateTimeKind.Unspecified);
    }
}

public class DateTimeOffsetTypeCouchbaseTest(DateTimeOffsetTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<DateTimeOffset, DateTimeOffsetTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<DateTimeOffset>
    {
        public override DateTimeOffset Value => new(2020, 1, 5, 12, 30, 45, TimeSpan.FromHours(2));
        public override DateTimeOffset OtherValue => new(2020, 1, 5, 12, 30, 45, TimeSpan.FromHours(3));
    }
}

public class DateOnlyTypeCouchbaseTest(DateOnlyTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<DateOnly, DateOnlyTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<DateOnly>
    {
        public override DateOnly Value => new(2020, 1, 5);
        public override DateOnly OtherValue => new(2022, 5, 3);
    }
}

public class TimeOnlyTypeCouchbaseTest(TimeOnlyTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<TimeOnly, TimeOnlyTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<TimeOnly>
    {
        public override TimeOnly Value => new(12, 30, 45);
        public override TimeOnly OtherValue => new(14, 0, 0);
    }
}

public class TimeSpanTypeCouchbaseTest(TimeSpanTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<TimeSpan, TimeSpanTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<TimeSpan>
    {
        public override TimeSpan Value => new(12, 30, 45);
        public override TimeSpan OtherValue => new(14, 0, 0);
    }
}

public class ByteArrayTypeCouchbaseTest(ByteArrayTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<byte[], ByteArrayTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<byte[]>
    {
        public override byte[] Value => [1, 2, 3];
        public override byte[] OtherValue => [4, 5, 6, 7];
    }
}

public enum CouchbaseTestEnum
{
    One,
    Two
}

public class EnumTypeCouchbaseTest(EnumTypeCouchbaseTest.Fixture fixture, ITestOutputHelper output)
    : RelationalTypeTestBase<CouchbaseTestEnum, EnumTypeCouchbaseTest.Fixture>(fixture, output)
{
    public class Fixture : CouchbaseTypeFixture<CouchbaseTestEnum>
    {
        public override CouchbaseTestEnum Value => CouchbaseTestEnum.One;
        public override CouchbaseTestEnum OtherValue => CouchbaseTestEnum.Two;
    }
}
