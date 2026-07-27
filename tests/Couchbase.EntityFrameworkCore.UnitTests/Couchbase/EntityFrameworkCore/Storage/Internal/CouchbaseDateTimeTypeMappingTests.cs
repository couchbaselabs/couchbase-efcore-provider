using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDateTimeTypeMappingTests
{
    [Fact]
    public void Constructor_SetsStoreTypeToString()
    {
        var mapping = new CouchbaseDateTimeTypeMapping("yyyy-MM-ddTHH:mm:ss.FFFK");

        Assert.Equal("STRING", mapping.StoreType);
    }

    [Fact]
    public void Constructor_SetsClrTypeToDateTime()
    {
        var mapping = new CouchbaseDateTimeTypeMapping("yyyy-MM-ddTHH:mm:ss.FFFK");

        Assert.Equal(typeof(DateTime), mapping.ClrType);
    }

    [Fact]
    public void GenerateSqlLiteral_UsesConfiguredFormat_NotStockTimestampSyntax()
    {
        // The stock EF Core DateTimeTypeMapping this class replaces emits
        // TIMESTAMP 'yyyy-MM-dd HH:mm:ss.fffffff' -- a syntax no N1QL date-string convention uses.
        var mapping = new CouchbaseDateTimeTypeMapping("yyyy-MM-ddTHH:mm:ss.FFFK");
        var value = new DateTime(2026, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);

        var literal = mapping.GenerateSqlLiteral(value);

        Assert.DoesNotContain("TIMESTAMP", literal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("'2026-03-14T09:26:53.123Z'", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithDifferentConfiguredFormat_UsesThatFormat()
    {
        var mapping = new CouchbaseDateTimeTypeMapping("yyyy-MM-dd");
        var value = new DateTime(2026, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);

        var literal = mapping.GenerateSqlLiteral(value);

        Assert.Equal("'2026-03-14'", literal);
    }

    [Fact]
    public void GenerateSqlLiteral_WithNull_ReturnsNULL()
    {
        var mapping = new CouchbaseDateTimeTypeMapping("yyyy-MM-ddTHH:mm:ss.FFFK");

        var literal = mapping.GenerateSqlLiteral(null);

        Assert.Equal("NULL", literal);
    }

    [Fact]
    public void Clone_PreservesConfiguredFormat()
    {
        var original = new CouchbaseDateTimeTypeMapping("yyyy-MM-dd");
        var cloned = (CouchbaseDateTimeTypeMapping)original.WithComposedConverter(null);
        var value = new DateTime(2026, 3, 14, 9, 26, 53, 123, DateTimeKind.Utc);

        Assert.Equal("'2026-03-14'", cloned.GenerateSqlLiteral(value));
    }

    [Fact]
    public void Mapping_InheritsDateTimeTypeMapping()
    {
        var mapping = new CouchbaseDateTimeTypeMapping("yyyy-MM-ddTHH:mm:ss.FFFK");

        Assert.IsAssignableFrom<DateTimeTypeMapping>(mapping);
    }
}
