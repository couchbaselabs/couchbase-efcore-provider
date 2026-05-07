using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Tests for verifying that boolean annotations can be properly read from EF model.
/// </summary>
public class BoolAnnotationReadingTests
{
    [Fact]
    public void PatternMatching_ReadsBoxedBoolCorrectly()
    {
        // Arrange - simulate how EF stores annotation values (boxed)
        object boxedFalse = false;
        object boxedTrue = true;

        // Act - using pattern matching
        var readFalse = boxedFalse is bool b1 ? b1 : true;
        var readTrue = boxedTrue is bool b2 ? b2 : false;

        // Assert
        Assert.False(readFalse);
        Assert.True(readTrue);
    }

    [Fact]
    public void PatternMatching_NullDefaultsCorrectly()
    {
        // Arrange - simulate missing annotation
        object? nullValue = null;

        // Act - using pattern matching with default
        var result = nullValue is bool b ? b : true;

        // Assert - defaults to true when null
        Assert.True(result);
    }

    [Fact]
    public void AnnotationValue_AutoCreateFalse_CanBeRead()
    {
        // Arrange - create model builder and set annotation like the convention does
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation, false);

        var model = modelBuilder.FinalizeModel();
        var property = model.FindEntityType(typeof(TestEntity))!.FindProperty(nameof(TestEntity.Id))!;

        // Act - read the annotation using pattern matching
        var annotation = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation);
        var autoCreate = annotation?.Value is bool b ? b : true;

        // Assert
        Assert.NotNull(annotation);
        Assert.False(autoCreate);
    }

    [Fact]
    public void AnnotationValue_AutoCreateTrue_CanBeRead()
    {
        // Arrange - create model builder and set annotation
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation, true);

        var model = modelBuilder.FinalizeModel();
        var property = model.FindEntityType(typeof(TestEntity))!.FindProperty(nameof(TestEntity.Id))!;

        // Act - read the annotation using pattern matching
        var annotation = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation);
        var autoCreate = annotation?.Value is bool b ? b : true;

        // Assert
        Assert.NotNull(annotation);
        Assert.True(autoCreate);
    }

    [Fact]
    public void AnnotationValue_MissingAnnotation_DefaultsToTrue()
    {
        // Arrange - create model builder without the annotation
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, "test_seq"); // Set some other annotation

        var model = modelBuilder.FinalizeModel();
        var property = model.FindEntityType(typeof(TestEntity))!.FindProperty(nameof(TestEntity.Id))!;

        // Act - read the annotation (should be null)
        var annotation = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation);
        var autoCreate = annotation?.Value is bool b ? b : true;

        // Assert - defaults to true when annotation is missing
        Assert.Null(annotation);
        Assert.True(autoCreate);
    }

    private class TestEntity
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }
}
