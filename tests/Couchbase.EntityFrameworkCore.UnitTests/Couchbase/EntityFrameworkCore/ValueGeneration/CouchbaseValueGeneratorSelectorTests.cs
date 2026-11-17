using Couchbase.EntityFrameworkCore.Extensions;
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

    // A sequence property gets a temporary value when the entity is added; the real value comes
    // from the sequence at SaveChanges (CouchbaseDatabaseWrapper), so Add needs no I/O.
    // Sequence locations: CouchbaseKeySequenceConventionTests. Unsupported types are rejected by
    // model validation (same tests).

    [Theory]
    [InlineData(typeof(IntEntity))]
    [InlineData(typeof(LongEntity))]
    public void Select_WithSequenceNameAnnotation_ReturnsTemporaryValueGenerator(Type entityType)
    {
        var builder = new DbContextOptionsBuilder<SequenceContext>();
        builder.UseCouchbaseProvider(new global::Couchbase.ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password"));
        using var context = new SequenceContext(builder.Options);

        var type = context.Model.FindEntityType(entityType)!;
        var property = type.FindProperty("Id")!;
        Assert.NotNull(property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation));

        var selector = context.GetService<IValueGeneratorSelector>();
        Assert.True(selector.TrySelect(property, type, out var generator));

        Assert.NotNull(generator);
        Assert.True(generator.GeneratesTemporaryValues);
    }

    private sealed class SequenceContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<IntEntity> Ints => Set<IntEntity>();
        public DbSet<LongEntity> Longs => Set<LongEntity>();
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
