using System.Globalization;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Tests for the <see cref="CouchbaseDocumentKeyFormat.V2"/> document key and the options that
/// select it. The legacy format is covered by <c>EntityTypeExtensionsTests</c>.
/// </summary>
public class CouchbaseDocumentKeyBuilderTests
{
    // Declared User then Tenant: the legacy format would order the parts by the REVERSE of this.
    private class MemberEntity
    {
        public int UserId { get; set; }
        public int TenantId { get; set; }
    }

    private class MemberEntityUserFirst
    {
        public int UserId { get; set; }
        public int TenantId { get; set; }
    }

    private class TimeKeyEntity
    {
        public TimeOnly At { get; set; }
        public DateOnly On { get; set; }
    }

    private class UnixMillisKeyEntity
    {
        public DateTime When { get; set; }
        public int Part { get; set; }
    }

    private class ConvertedBlobKeyEntity
    {
        public byte[] Blob { get; set; } = [];
        public int Part { get; set; }
    }

    private class LocalTimeKeyEntity
    {
        public DateTime When { get; set; }
    }

    private class EmptyTextKeyEntity
    {
        public string Code { get; set; } = "";
    }

    private class BlobKeyEntity
    {
        public byte[] Blob { get; set; } = [];
    }

    private class TextKeyEntity
    {
        public string A { get; set; } = "";
        public string B { get; set; } = "";
    }

    private class SingleKeyEntity
    {
        public decimal Amount { get; set; }
    }

    private class MixedKeyEntity
    {
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
        public Mode Mode { get; set; }
        public byte[] Blob { get; set; } = [];
        public Guid Id { get; set; }
    }

    private enum Mode : short
    {
        Off = 0,
        On = 7,
    }

    private static IEntityType BuildEntityType<T>(Action<ModelBuilder> configure)
        where T : class
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<TestContext<T>>();
        builder.UseCouchbaseProvider(opts);

        using var ctx = new TestContext<T>(builder.Options, configure);
        return ctx.Model.FindEntityType(typeof(T))!;
    }

    private class TestContext<T>(DbContextOptions options, Action<ModelBuilder> configure) : DbContext(options)
        where T : class
    {
        public DbSet<T> Entities { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    }

    private static IUpdateEntry Entry(IEntityType entityType, params (string Name, object? Value)[] values)
    {
        var mock = new Mock<IUpdateEntry>();
        mock.Setup(e => e.EntityType).Returns(entityType);
        foreach (var (name, value) in values)
        {
            mock.Setup(e => e.GetCurrentValue(entityType.FindProperty(name)!)).Returns(value);
        }

        return mock.Object;
    }

    private static IEntityType TextKeyType() => BuildEntityType<TextKeyEntity>(mb =>
        mb.Entity<TextKeyEntity>(b =>
        {
            b.ToCouchbaseCollection("bucket", "scope", "text");
            b.HasKey(e => new { e.A, e.B });
        }));

    // One entity type per key order: EF Core caches the model per context type, so reusing a type
    // with a different HasKey would silently keep the first model.
    [Fact]
    public void V2_UsesTheDeclaredKeyOrder_NotTheClassDeclarationOrder()
    {
        var tenantFirst = BuildEntityType<MemberEntity>(mb =>
            mb.Entity<MemberEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "member");
                b.HasKey(e => new { e.TenantId, e.UserId });
            }));
        var userFirst = BuildEntityType<MemberEntityUserFirst>(mb =>
            mb.Entity<MemberEntityUserFirst>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "member");
                b.HasKey(e => new { e.UserId, e.TenantId });
            }));

        Assert.Equal(
            "7_1",
            CouchbaseDocumentKeyBuilder.BuildV2(tenantFirst, Entry(tenantFirst, ("TenantId", 7), ("UserId", 1)), '_'));
        Assert.Equal(
            "1_7",
            CouchbaseDocumentKeyBuilder.BuildV2(userFirst, Entry(userFirst, ("TenantId", 7), ("UserId", 1)), '_'));
    }

    [Fact]
    public void V2_SingleKey_HasNoDelimiter()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "single");
                b.HasKey(e => e.Amount);
            }));

        Assert.Equal("42", CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Amount", 42m)), '_'));
    }

    [Fact]
    public void V2_KeysThatCollideInLegacy_AreDistinct()
    {
        var entityType = TextKeyType();

        var first = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("A", "x_y"), ("B", "z")), '_');
        var second = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("A", "y"), ("B", "z_x")), '_');

        Assert.Equal(@"x\_y_z", first);
        Assert.Equal(@"y_z\_x", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void V2_EscapesTheEscapeCharacter_SoABackslashCannotMimicAnEscape()
    {
        var entityType = TextKeyType();

        // ("a\", "b") must differ from ("a_b" ...) style keys: the trailing backslash is doubled.
        var withBackslash = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("A", @"a\"), ("B", "b")), '_');
        var withDelimiter = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("A", "a_"), ("B", "b")), '_');

        Assert.Equal(@"a\\_b", withBackslash);
        Assert.Equal(@"a\__b", withDelimiter);
    }

    [Fact]
    public void V2_UsesTheConfiguredDelimiter_AndOnlyEscapesThatOne()
    {
        var entityType = TextKeyType();

        var key = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("A", "a_b|c"), ("B", "d")), '|');

        Assert.Equal(@"a_b\|c|d", key);
    }

    [Fact]
    public void V2_IgnoresTheCurrentCulture()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "single");
                b.HasKey(e => e.Amount);
            }));
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1234.5", CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Amount", 1234.5m)), '_'));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void V2_FormatsDatesBytesEnumsAndGuidsStably()
    {
        var entityType = BuildEntityType<MixedKeyEntity>(mb =>
            mb.Entity<MixedKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "mixed");
                b.HasKey(e => new { e.When, e.Offset, e.Mode, e.Blob, e.Id });
            }));
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var key = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType,
            Entry(
                entityType,
                ("When", new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Utc)),
                ("Offset", new DateTimeOffset(2025, 1, 15, 13, 45, 0, TimeSpan.FromHours(-8))),
                ("Mode", Mode.On),
                ("Blob", new byte[] { 1, 2, 3 }),
                ("Id", id)),
            '#');

        Assert.Equal(
            "2025-01-15T13:45:00.0000000Z#2025-01-15T13:45:00.0000000-08:00#7#AQID#11111111-2222-3333-4444-555555555555",
            key);
    }

    [Fact]
    public void V2_TimeOnlyAndDateOnly_KeepSecondsAndFractions_SoDistinctValuesGiveDistinctKeys()
    {
        var entityType = BuildEntityType<TimeKeyEntity>(mb =>
            mb.Entity<TimeKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "time");
                b.HasKey(e => new { e.On, e.At });
            }));
        var day = new DateOnly(2025, 1, 15);

        var first = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("On", day), ("At", new TimeOnly(12, 30, 1))), '_');
        var second = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("On", day), ("At", new TimeOnly(12, 30, 59))), '_');
        var fractional = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("On", day), ("At", new TimeOnly(12, 30, 1, 500))), '_');

        Assert.Equal("2025-01-15_12:30:01.0000000", first);
        Assert.Equal("2025-01-15_12:30:59.0000000", second);
        Assert.Equal("2025-01-15_12:30:01.5000000", fractional);
    }

    [Fact]
    public void V2_ConvertedKey_IsFormattedFromTheStoredValue_SoItSurvivesAReload()
    {
        var entityType = BuildEntityType<UnixMillisKeyEntity>(mb =>
            mb.Entity<UnixMillisKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "millis");
                b.Property(e => e.When).HasUnixMillisDateTime();
                b.HasKey(e => new { e.When, e.Part });
            }));
        var inserted = new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Utc).AddTicks(1234);
        // What reading the stored (millisecond-truncated) value back gives.
        var reloaded = new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Utc);

        var insertedKey = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("When", inserted), ("Part", 1)), '_');
        var reloadedKey = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("When", reloaded), ("Part", 1)), '_');

        Assert.Equal(reloadedKey, insertedKey);
        Assert.Equal(new DateTimeOffset(reloaded).ToUnixTimeMilliseconds() + "_1", insertedKey);
    }

    [Fact]
    public void V2_CustomByteArrayConverter_IsHonored_SoAConvertedTildeDoesNotCollideWithAnEmptyArray()
    {
        var entityType = BuildEntityType<ConvertedBlobKeyEntity>(mb =>
            mb.Entity<ConvertedBlobKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "convertedblob");
                b.Property(e => e.Blob).HasConversion(
                    v => System.Text.Encoding.UTF8.GetString(v),
                    v => System.Text.Encoding.UTF8.GetBytes(v));
                b.HasKey(e => new { e.Blob, e.Part });
            }));

        var empty = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("Blob", Array.Empty<byte>()), ("Part", 1)), '_');
        var tilde = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("Blob", new byte[] { 0x7e }), ("Part", 1)), '_');

        Assert.Equal("_1", empty);
        Assert.Equal("~_1", tilde);
        Assert.NotEqual(empty, tilde);
    }

    [Fact]
    public void V2_LocalDateTime_FormatsAsTheSameInstantAsUtc_RegardlessOfTheMachineTimeZone()
    {
        var entityType = BuildEntityType<LocalTimeKeyEntity>(mb =>
            mb.Entity<LocalTimeKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "localtime");
                b.HasKey(e => e.When);
            }));
        var utc = new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Utc);

        var fromUtc = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("When", utc)), '_');
        var fromLocal = CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("When", utc.ToLocalTime())), '_');
        var unspecified = CouchbaseDocumentKeyBuilder.BuildV2(
            entityType, Entry(entityType, ("When", new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Unspecified))), '_');

        Assert.Equal("2025-01-15T13:45:00.0000000Z", fromUtc);
        Assert.Equal(fromUtc, fromLocal);
        Assert.Equal("2025-01-15T13:45:00.0000000", unspecified);
    }

    [Fact]
    public void V2_Decimal_IsCanonical_SoATrailingZeroSurvivesAReload()
    {
        var entityType = BuildEntityType<SingleKeyEntity>(mb =>
            mb.Entity<SingleKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "single");
                b.HasKey(e => e.Amount);
            }));

        Assert.Equal("1234.5", CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Amount", 1234.50m)), '_'));
        Assert.Equal("1234.5", CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Amount", 1234.5m)), '_'));
        Assert.Equal("100", CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Amount", 100m)), '_'));
    }

    [Fact]
    public void V2_EmptyDocumentKey_ThrowsAClearError()
    {
        var entityType = BuildEntityType<EmptyTextKeyEntity>(mb =>
            mb.Entity<EmptyTextKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "emptytext");
                b.HasKey(e => e.Code);
            }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Code", "")), '_'));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void V2_EmptyByteArray_IsNonEmpty()
    {
        var entityType = BuildEntityType<BlobKeyEntity>(mb =>
            mb.Entity<BlobKeyEntity>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "blob");
                b.HasKey(e => e.Blob);
            }));

        Assert.Equal("~", CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("Blob", Array.Empty<byte>())), '_'));
    }

    [Fact]
    public void V2_NullKeyPart_Throws()
    {
        var entityType = TextKeyType();

        var ex = Assert.Throws<InvalidOperationException>(
            () => CouchbaseDocumentKeyBuilder.BuildV2(entityType, Entry(entityType, ("A", "x"), ("B", null)), '_'));

        Assert.Contains("B", ex.Message);
    }

    [Fact]
    public void Options_DefaultToLegacyWithAnUnderscoreDelimiter()
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");

        Assert.Equal(CouchbaseDocumentKeyFormat.Legacy, builder.DocumentKeyFormat);
        Assert.Equal('_', builder.DocumentKeyDelimiter);
    }

    [Fact]
    public void UseDocumentKeyFormat_SetsTheFormatAndDelimiter()
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");

        builder.UseDocumentKeyFormat(CouchbaseDocumentKeyFormat.V2, '|');

        Assert.Equal(CouchbaseDocumentKeyFormat.V2, builder.DocumentKeyFormat);
        Assert.Equal('|', builder.DocumentKeyDelimiter);
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('\n')]
    [InlineData('\0')]
    public void DocumentKeyDelimiter_RejectsTheEscapeAndControlCharacters(char delimiter)
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");

        Assert.Throws<ArgumentException>(() => builder.DocumentKeyDelimiter = delimiter);
    }
}
