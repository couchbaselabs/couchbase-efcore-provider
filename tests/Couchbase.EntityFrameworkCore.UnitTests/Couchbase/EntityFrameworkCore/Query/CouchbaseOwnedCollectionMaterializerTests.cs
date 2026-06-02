using System.Text.Json;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Unit tests for <see cref="CouchbaseOwnedCollectionMaterializer"/>.
/// These tests exercise <see cref="CouchbaseOwnedCollectionMaterializer.MaterializeOwnedItem"/>
/// and <see cref="CouchbaseOwnedCollectionMaterializer.ConvertJsonValue"/> directly — no live
/// Couchbase server or EF Core DbContext required.
/// </summary>
public class CouchbaseOwnedCollectionMaterializerTests
{
    private static readonly JsonSerializerOptions _webOptions = new(JsonSerializerDefaults.Web);

    // -------------------------------------------------------------------------
    // ConvertJsonValue — scalar type mapping
    // -------------------------------------------------------------------------

    [Fact]
    public void ConvertJsonValue_String_ReturnsString()
    {
        var el = JsonDocument.Parse("\"hello\"").RootElement;
        Assert.Equal("hello", CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(string), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Int_ReturnsInt()
    {
        var el = JsonDocument.Parse("42").RootElement;
        Assert.Equal(42, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(int), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Long_ReturnsLong()
    {
        var el = JsonDocument.Parse("9999999999").RootElement;
        Assert.Equal(9999999999L, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(long), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Double_ReturnsDouble()
    {
        var el = JsonDocument.Parse("3.14").RootElement;
        Assert.Equal(3.14, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(double), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Float_ReturnsFloat()
    {
        var el = JsonDocument.Parse("1.5").RootElement;
        Assert.Equal(1.5f, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(float), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Decimal_ReturnsDecimal()
    {
        var el = JsonDocument.Parse("123.456").RootElement;
        Assert.Equal(123.456m, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(decimal), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Bool_ReturnsBool()
    {
        var t = JsonDocument.Parse("true").RootElement;
        var f = JsonDocument.Parse("false").RootElement;
        Assert.Equal(true,  CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(t, typeof(bool), _webOptions));
        Assert.Equal(false, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(f, typeof(bool), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_Guid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();
        var el = JsonDocument.Parse($"\"{guid}\"").RootElement;
        Assert.Equal(guid, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(Guid), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_DateTime_ReturnsDateTime()
    {
        var el = JsonDocument.Parse("\"2024-01-15T12:00:00\"").RootElement;
        var result = CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(DateTime), _webOptions);
        Assert.Equal(new DateTime(2024, 1, 15, 12, 0, 0), result);
    }

    [Fact]
    public void ConvertJsonValue_NullableInt_ReturnsInt()
    {
        var el = JsonDocument.Parse("7").RootElement;
        Assert.Equal(7, CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(int?), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_JsonNull_ReturnsNull()
    {
        var el = JsonDocument.Parse("null").RootElement;
        Assert.Null(CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(string), _webOptions));
        Assert.Null(CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(int?), _webOptions));
    }

    [Fact]
    public void ConvertJsonValue_UnknownType_FallsBackToJsonSerializer()
    {
        // byte[] is not in the switch — falls back to JsonSerializer.Deserialize.
        // System.Text.Json deserialises byte[] from a base-64 string, not a JSON array.
        var bytes = new byte[] { 1, 2, 3 };
        var base64 = Convert.ToBase64String(bytes);
        var el = JsonDocument.Parse($"\"{base64}\"").RootElement;
        var result = CouchbaseOwnedCollectionMaterializer.ConvertJsonValue(el, typeof(byte[]), _webOptions);
        var typed = Assert.IsType<byte[]>(result);
        Assert.Equal(bytes, typed);
    }

    // -------------------------------------------------------------------------
    // MaterializeOwnedItem — requires EF Core IEntityType, so tested via
    // the existing OwnedCollectionInjectionTests model contexts
    // (full coverage is in integration tests; unit coverage below focuses on
    // the cases that do NOT need a live DB)
    // -------------------------------------------------------------------------

    // Note: MaterializeOwnedItem requires IEntityType from an EF Core model, which
    // requires model-building infrastructure. Those scenarios are covered by the
    // existing integration tests (OwnedTypeTests). The pure JSON → CLR conversion
    // path is covered above via ConvertJsonValue, which MaterializeOwnedItem delegates to.
}
