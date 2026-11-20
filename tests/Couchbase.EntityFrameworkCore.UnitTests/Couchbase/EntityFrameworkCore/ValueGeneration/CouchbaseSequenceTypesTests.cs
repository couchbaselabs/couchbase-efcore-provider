using Couchbase.EntityFrameworkCore.ValueGeneration;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseSequenceTypesTests
{
    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(short))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(decimal))]
    public void IsSupported_ForSupportedTypes_ReturnsTrue(Type type)
    {
        // Act & Assert
        Assert.True(CouchbaseSequenceTypes.IsSupported(type));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(bool))]
    public void IsSupported_ForUnsupportedTypes_ReturnsFalse(Type type)
    {
        // Act & Assert
        Assert.False(CouchbaseSequenceTypes.IsSupported(type));
    }
}
