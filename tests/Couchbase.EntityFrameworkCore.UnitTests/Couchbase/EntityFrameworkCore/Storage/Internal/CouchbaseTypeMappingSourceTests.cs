using System.Text.Json.Nodes;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseTypeMappingSourceTests
{
    private readonly CouchbaseTypeMappingSource _typeMappingSource;

    public CouchbaseTypeMappingSourceTests()
    {
        var mockJsonReaderWriterSource = new Mock<IJsonValueReaderWriterSource>();
        mockJsonReaderWriterSource
            .Setup(x => x.FindReaderWriter(It.IsAny<Type>()))
            .Returns((JsonValueReaderWriter?)null);

        var mockValueConverterSelector = new Mock<IValueConverterSelector>();

        var dependencies = new TypeMappingSourceDependencies(
            mockValueConverterSelector.Object,
            mockJsonReaderWriterSource.Object,
            Array.Empty<ITypeMappingSourcePlugin>());

        var relationalDependencies = new RelationalTypeMappingSourceDependencies(
            Array.Empty<IRelationalTypeMappingSourcePlugin>());

        _typeMappingSource = new CouchbaseTypeMappingSource(dependencies, relationalDependencies);
    }

    #region Numeric Types -> NUMBER

    [Fact]
    public void FindMapping_Int_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(int));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(int), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_UInt_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(uint));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(uint), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Long_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(long));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(long), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_ULong_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(ulong));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(ulong), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Short_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(short));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(short), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_UShort_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(ushort));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(ushort), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Byte_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(byte));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(byte), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_SByte_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(sbyte));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(sbyte), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Float_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(float));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(float), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Double_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(double));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(double), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Decimal_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(decimal));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
        Assert.Equal(typeof(decimal), mapping.ClrType);
    }

    #endregion

    #region Boolean -> BOOLEAN

    [Fact]
    public void FindMapping_Bool_ReturnsBooleanType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(bool));

        Assert.NotNull(mapping);
        Assert.Equal("BOOLEAN", mapping.StoreType);
        Assert.Equal(typeof(bool), mapping.ClrType);
    }

    #endregion

    #region String Types -> STRING

    [Fact]
    public void FindMapping_String_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(string));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(string), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_Char_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(char));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(char), mapping.ClrType);
    }

    #endregion

    #region Date/Time Types -> STRING

    [Fact]
    public void FindMapping_DateTime_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(DateTime));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(DateTime), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_DateTimeOffset_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(DateTimeOffset));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(DateTimeOffset), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_DateOnly_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(DateOnly));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(DateOnly), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_TimeOnly_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(TimeOnly));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(TimeOnly), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_TimeSpan_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(TimeSpan));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(TimeSpan), mapping.ClrType);
    }

    #endregion

    #region Other Types -> STRING

    [Fact]
    public void FindMapping_Guid_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(Guid));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(Guid), mapping.ClrType);
    }

    [Fact]
    public void FindMapping_ByteArray_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(byte[]));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
        Assert.Equal(typeof(byte[]), mapping.ClrType);
    }

    #endregion

    #region JSON Types

    [Fact]
    public void FindMapping_JsonObject_ReturnsObjectType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(JsonObject));

        Assert.NotNull(mapping);
        Assert.Equal(typeof(JsonObject), mapping.ClrType);
        Assert.Equal("OBJECT", mapping.StoreType);
        Assert.IsType<CouchbaseTypeMapping>(mapping);
    }

    [Fact]
    public void FindMapping_JsonArray_ReturnsArrayType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(JsonArray));

        Assert.NotNull(mapping);
        Assert.Equal(typeof(JsonArray), mapping.ClrType);
        Assert.Equal("ARRAY", mapping.StoreType);
        Assert.IsType<CouchbaseTypeMapping>(mapping);
    }

    #endregion

    #region Nullable Types

    [Fact]
    public void FindMapping_NullableInt_ReturnsNumberType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(int?));

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
    }

    [Fact]
    public void FindMapping_NullableBool_ReturnsBooleanType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(bool?));

        Assert.NotNull(mapping);
        Assert.Equal("BOOLEAN", mapping.StoreType);
    }

    [Fact]
    public void FindMapping_NullableDateTime_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(DateTime?));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
    }

    [Fact]
    public void FindMapping_NullableGuid_ReturnsStringType()
    {
        var mapping = _typeMappingSource.FindMapping(typeof(Guid?));

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
    }

    #endregion

    #region Theory Tests for All Numeric Types

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    public void FindMapping_NumericTypes_AllMapToNumber(Type clrType)
    {
        var mapping = _typeMappingSource.FindMapping(clrType);

        Assert.NotNull(mapping);
        Assert.Equal("NUMBER", mapping.StoreType);
    }

    [Theory]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(TimeOnly))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(string))]
    [InlineData(typeof(char))]
    [InlineData(typeof(byte[]))]
    public void FindMapping_StringSerializedTypes_AllMapToString(Type clrType)
    {
        var mapping = _typeMappingSource.FindMapping(clrType);

        Assert.NotNull(mapping);
        Assert.Equal("STRING", mapping.StoreType);
    }

    #endregion
}
