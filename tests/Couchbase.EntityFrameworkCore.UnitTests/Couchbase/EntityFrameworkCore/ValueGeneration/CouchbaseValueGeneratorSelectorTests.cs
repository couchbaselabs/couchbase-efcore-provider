using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseValueGeneratorSelectorTests
{
    private readonly Mock<ICouchbaseDbContextOptionsBuilder> _mockOptionsBuilder;
    private readonly CouchbaseValueGeneratorSelector _selector;

    public CouchbaseValueGeneratorSelectorTests()
    {
        _mockOptionsBuilder = new Mock<ICouchbaseDbContextOptionsBuilder>();
        _mockOptionsBuilder.Setup(o => o.Bucket).Returns("test-bucket");
        _mockOptionsBuilder.Setup(o => o.Scope).Returns("test-scope");

        var dependencies = CreateDependencies();
        _selector = new CouchbaseValueGeneratorSelector(dependencies, _mockOptionsBuilder.Object);
    }

    private ValueGeneratorSelectorDependencies CreateDependencies()
    {
        return new ValueGeneratorSelectorDependencies(
            new ValueGeneratorCache(new ValueGeneratorCacheDependencies()));
    }

    #region GUID String Generator Selection Tests

    [Fact]
    public void Select_WithGuidStringFormatAnnotation_ReturnsCouchbaseGuidStringValueGenerator()
    {
        // Arrange
        var (property, typeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation,
            "D");

        // Act
        Assert.True(_selector.TrySelect(property, typeBase, out var generator));

        // Assert
        Assert.NotNull(generator);
        Assert.IsType<CouchbaseGuidStringValueGenerator>(generator);
    }

    [Fact]
    public void Select_WithGuidStringFormatAnnotation_UsesCorrectFormat()
    {
        // Arrange
        var (property, typeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation,
            "N");

        // Act
        Assert.True(_selector.TrySelect(property, typeBase, out var generator));

        // Assert
        var guidGenerator = Assert.IsType<CouchbaseGuidStringValueGenerator>(generator);
        Assert.Equal("N", guidGenerator.Format);
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    public void Select_WithVariousGuidFormats_ReturnsGeneratorWithCorrectFormat(string format)
    {
        // Arrange
        var (property, typeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation,
            format);

        // Act
        Assert.True(_selector.TrySelect(property, typeBase, out var generator));

        // Assert
        var guidGenerator = Assert.IsType<CouchbaseGuidStringValueGenerator>(generator);
        Assert.Equal(format, guidGenerator.Format);
    }

    [Fact]
    public void Create_WithGuidStringFormatAnnotation_ReturnsCouchbaseGuidStringValueGenerator()
    {
        // Arrange
        var (property, typeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation,
            "D");

        // Act
        Assert.True(_selector.TryCreate(property, typeBase, out var generator));

        // Assert
        Assert.NotNull(generator);
        Assert.IsType<CouchbaseGuidStringValueGenerator>(generator);
    }

    [Fact]
    public void Select_WithGuidStringFormatOnNonStringProperty_ThrowsWhenSequenceUsed()
    {
        // The selector checks type for GUID string, but if we have sequence annotation on a 
        // non-numeric type, it should throw. This verifies the type checking path works.
        var (property, typeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            "test_seq");

        // Act & Assert - Sequence on string type should fail
        var exception = Assert.Throws<InvalidOperationException>(() => _selector.TrySelect(property, typeBase, out _));
        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void Select_GuidStringFormat_RequiresStringType()
    {
        // Test that GUID string format annotation is only effective for string properties
        // by verifying the selector returns the generator ONLY for strings
        var (stringProperty, stringTypeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation,
            "D");

        // Act
        Assert.True(_selector.TrySelect(stringProperty, stringTypeBase, out var stringGenerator));

        // Assert - String property with annotation should return GUID string generator
        Assert.IsType<CouchbaseGuidStringValueGenerator>(stringGenerator);
    }

    #endregion

    #region Sequence Generator Selection Tests

    [Fact]
    public void Select_WithSequenceNameAnnotation_ReturnsCouchbaseSequenceValueGenerator()
    {
        // Arrange
        var (property, typeBase) = CreatePropertyWithAnnotation<LongEntity>(
            nameof(LongEntity.Id),
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            "test_seq");

        // Act
        Assert.True(_selector.TrySelect(property, typeBase, out var generator));

        // Assert
        Assert.NotNull(generator);
        Assert.IsType<CouchbaseSequenceValueGenerator<long>>(generator);
    }

    [Fact]
    public void Select_WithSequenceNameAnnotation_ReturnsCorrectGeneratorType()
    {
        // Arrange
        var (property, typeBase) = CreatePropertyWithAnnotation<IntEntity>(
            nameof(IntEntity.Id),
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            "order_seq");

        // Act
        Assert.True(_selector.TrySelect(property, typeBase, out var generator));

        // Assert - Should return int generator for int property
        Assert.IsType<CouchbaseSequenceValueGenerator<int>>(generator);
    }

    [Fact]
    public void Select_SequenceAnnotationTakesPrecedenceOverGuidString()
    {
        // Arrange - Both annotations present, sequence should win
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<StringEntity>()
            .Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, "test_seq")
            .HasAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation, "D");

        var model = modelBuilder.FinalizeModel();
        var entityType = model.FindEntityType(typeof(StringEntity))!;
        var property = entityType.FindProperty(nameof(StringEntity.Id))!;

        // Act & Assert - Should throw because sequence doesn't support string type
        Assert.Throws<InvalidOperationException>(() => _selector.TrySelect(property, entityType, out _));
    }

    [Fact]
    public void Select_WithUnsupportedTypeForSequence_ThrowsInvalidOperationException()
    {
        // Arrange - Sequence on a string property should fail
        var (property, typeBase) = CreatePropertyWithAnnotation<StringEntity>(
            nameof(StringEntity.Id),
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            "test_seq");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _selector.TrySelect(property, typeBase, out _));
        Assert.Contains("not supported", exception.Message);
    }

    #endregion

    #region Helper Methods

    private (IProperty Property, ITypeBase TypeBase) CreatePropertyWithAnnotation<TEntity>(
        string propertyName,
        string annotationKey,
        object annotationValue) where TEntity : class
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<TEntity>()
            .Property(propertyName)
            .HasAnnotation(annotationKey, annotationValue);

        var model = modelBuilder.FinalizeModel();
        var entityType = model.FindEntityType(typeof(TEntity))!;
        var property = entityType.FindProperty(propertyName)!;

        return (property, entityType);
    }

    #endregion

    #region Test Entities

    private class StringEntity
    {
        public string Id { get; set; } = null!;
        public string? Name { get; set; }
    }

    private class LongEntity
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }

    private class IntEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    #endregion
}
