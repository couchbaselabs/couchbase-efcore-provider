using Couchbase.EntityFrameworkCore.Metadata;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Metadata;

public class CouchbaseSequenceAttributeTests
{
    [Fact]
    public void Constructor_WithSequenceName_SetsSequenceName()
    {
        // Arrange & Act
        var attribute = new CouchbaseSequenceAttribute("order_seq");

        // Assert
        Assert.Equal("order_seq", attribute.SequenceName);
        Assert.Null(attribute.Scope);
    }

    [Fact]
    public void Constructor_WithScopeAndSequenceName_SetsBothProperties()
    {
        // Arrange & Act
        var attribute = new CouchbaseSequenceAttribute("analytics", "order_seq");

        // Assert
        Assert.Equal("order_seq", attribute.SequenceName);
        Assert.Equal("analytics", attribute.Scope);
    }

    [Fact]
    public void Constructor_WithNullSequenceName_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceAttribute(null!));
    }

    [Fact]
    public void Constructor_WithEmptySequenceName_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceAttribute(""));
    }

    [Fact]
    public void Constructor_WithNullScope_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceAttribute(null!, "order_seq"));
    }

    [Fact]
    public void Constructor_WithEmptyScope_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceAttribute("", "order_seq"));
    }

    [Fact]
    public void Constructor_WithNullSequenceNameInTwoArgVersion_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CouchbaseSequenceAttribute("scope", null!));
    }

    [Fact]
    public void Constructor_WithEmptySequenceNameInTwoArgVersion_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new CouchbaseSequenceAttribute("scope", ""));
    }

    [Fact]
    public void Attribute_CanBeAppliedToProperty()
    {
        // Arrange
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Id));

        // Act
        var attributes = propertyInfo!.GetCustomAttributes(typeof(CouchbaseSequenceAttribute), false);

        // Assert
        Assert.Single(attributes);
        var attribute = (CouchbaseSequenceAttribute)attributes[0];
        Assert.Equal("test_seq", attribute.SequenceName);
    }

    [Fact]
    public void Attribute_WithScope_CanBeAppliedToProperty()
    {
        // Arrange
        var propertyInfo = typeof(TestEntityWithScope).GetProperty(nameof(TestEntityWithScope.Id));

        // Act
        var attributes = propertyInfo!.GetCustomAttributes(typeof(CouchbaseSequenceAttribute), false);

        // Assert
        Assert.Single(attributes);
        var attribute = (CouchbaseSequenceAttribute)attributes[0];
        Assert.Equal("counter_seq", attribute.SequenceName);
        Assert.Equal("counters", attribute.Scope);
    }

    private class TestEntity
    {
        [CouchbaseSequence("test_seq")]
        public long Id { get; set; }
    }

    private class TestEntityWithScope
    {
        [CouchbaseSequence("counters", "counter_seq")]
        public long Id { get; set; }
    }
}
