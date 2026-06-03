using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore;
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

    // MaterializeOwnedItem — unit tests using a Couchbase provider context so
    // the model is fully finalised (same path as production queries).

    // CLR types used by the MaterializeOwnedItem unit tests.
    private class ItemOwner
    {
        public int Id { get; set; }
        public List<OwnedItem> Items { get; set; } = [];
        public FieldItem? Single { get; set; }
    }

    private class OwnedItem
    {
        public int Id { get; set; }
        public string? Value { get; set; }
        public HashSet<NestedTag> Tags { get; set; } = [];
    }

    private class NestedTag
    {
        public int Id { get; set; }
        public string Key { get; set; } = "";
    }

    /// <summary>OwnsOne with a get-only scalar — no setter, EF must use backing field.</summary>
    private class FieldItem
    {
        public string? Label { get; }
        public FieldItem() { }
        public FieldItem(string? label) { Label = label; }
    }

    private class ItemContext(DbContextOptions<ItemContext> options) : DbContext(options)
    {
        public DbSet<ItemOwner> Owners { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ItemOwner>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "owner");
                b.HasKey(o => o.Id);
                b.OwnsOne(o => o.Single, fi =>
                    fi.Property(x => x.Label).UsePropertyAccessMode(PropertyAccessMode.Field));
                b.OwnsMany(o => o.Items, cm =>
                {
                    cm.HasKey(i => i.Id);
                    cm.OwnsMany(i => i.Tags, t => t.HasKey(t => t.Id));
                });
            });
        }
    }

    private static ItemContext CreateItemContext()
    {
        var opts = new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<ItemContext>();
        builder.UseCouchbaseProvider(opts);
        return new ItemContext(builder.Options);
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType GetOwnedType<T>(ItemContext ctx)
        => ctx.Model.GetEntityTypes().First(e => e.ClrType == typeof(T));

    [Fact]
    public void MaterializeOwnedItem_ScalarProperties_AreSet()
    {
        using var ctx = CreateItemContext();
        var itemType = GetOwnedType<OwnedItem>(ctx);
        var policy  = JsonNamingPolicy.CamelCase;

        var json = JsonDocument.Parse("""{"id":7,"value":"hello"}""").RootElement;
        var result = CouchbaseOwnedCollectionMaterializer.MaterializeOwnedItem(json, itemType, policy, _webOptions);

        var item = Assert.IsType<OwnedItem>(result);
        Assert.Equal(7, item.Id);
        Assert.Equal("hello", item.Value);
    }

    [Fact]
    public void MaterializeOwnedItem_GetOnlyScalar_UsesFieldInfoFallback()
    {
        // FieldItem.Label has no setter — EF must write via the backing field.
        using var ctx = CreateItemContext();
        var fieldType = GetOwnedType<FieldItem>(ctx);
        var policy    = JsonNamingPolicy.CamelCase;

        var json = JsonDocument.Parse("""{"label":"work"}""").RootElement;
        var result = CouchbaseOwnedCollectionMaterializer.MaterializeOwnedItem(json, fieldType, policy, _webOptions);

        var item = Assert.IsType<FieldItem>(result);
        Assert.Equal("work", item.Label);
    }

    [Fact]
    public void MaterializeOwnedItem_NestedHashSet_IsPopulated()
    {
        // OwnedItem.Tags is a HashSet<NestedTag> — exercises the ICollection<T>
        // clear path in the nested navigation loop of MaterializeOwnedItem.
        using var ctx = CreateItemContext();
        var itemType = GetOwnedType<OwnedItem>(ctx);
        var policy   = JsonNamingPolicy.CamelCase;

        var json = JsonDocument.Parse("""{"id":1,"value":"x","tags":[{"id":1,"key":"a"},{"id":2,"key":"b"}]}""").RootElement;
        var result = CouchbaseOwnedCollectionMaterializer.MaterializeOwnedItem(json, itemType, policy, _webOptions);

        var item = Assert.IsType<OwnedItem>(result);
        Assert.Equal(2, item.Tags.Count);
        Assert.Contains(item.Tags, t => t.Key == "a");
        Assert.Contains(item.Tags, t => t.Key == "b");
    }

    [Fact]
    public void MaterializeOwnedItem_NestedHashSet_NoDuplicatesOnRepeatCall()
    {
        // Calling MaterializeOwnedItem twice on a JSON element with the same item
        // must not accumulate duplicates in the HashSet — the clear must run.
        using var ctx = CreateItemContext();
        var itemType = GetOwnedType<OwnedItem>(ctx);
        var policy   = JsonNamingPolicy.CamelCase;

        var json = JsonDocument.Parse("""{"id":1,"value":"x","tags":[{"id":1,"key":"a"}]}""").RootElement;

        // First materialisation
        var result1 = (OwnedItem)CouchbaseOwnedCollectionMaterializer.MaterializeOwnedItem(json, itemType, policy, _webOptions);
        Assert.Single(result1.Tags);

        // Second materialisation of the same JSON must also yield exactly 1 tag.
        var result2 = (OwnedItem)CouchbaseOwnedCollectionMaterializer.MaterializeOwnedItem(json, itemType, policy, _webOptions);
        Assert.Single(result2.Tags);
    }

    // Note: MaterializeOwnedItem requires IEntityType from an EF Core model, which
    // requires model-building infrastructure. Those scenarios are covered by the
    // existing integration tests (OwnedTypeTests). The pure JSON → CLR conversion
    // path is covered above via ConvertJsonValue, which MaterializeOwnedItem delegates to.
}
