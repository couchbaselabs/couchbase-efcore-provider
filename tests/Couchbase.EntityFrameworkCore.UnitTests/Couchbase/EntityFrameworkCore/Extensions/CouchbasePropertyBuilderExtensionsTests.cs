using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Extensions;

public class CouchbasePropertyBuilderExtensionsTests
{
    [Fact]
    public void UseSequence_SetsSequenceNameAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseSequence("order_seq");

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        Assert.Equal("order_seq", sequenceName);
    }

    [Fact]
    public void UseSequence_SetsValueGeneratedOnAdd()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseSequence("order_seq");

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        Assert.Equal(ValueGenerated.OnAdd, property!.ValueGenerated);
    }

    [Fact]
    public void UseSequence_WithScope_SetsBothAnnotations()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseSequence("analytics", "order_seq");

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        var scopeName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value;
        
        Assert.Equal("order_seq", sequenceName);
        Assert.Equal("analytics", scopeName);
    }

    [Fact]
    public void UseSequence_WithNullSequenceName_ThrowsArgumentNullException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            entityBuilder.Property(e => e.Id).UseSequence(null!));
    }

    [Fact]
    public void UseSequence_WithEmptySequenceName_ThrowsArgumentException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            entityBuilder.Property(e => e.Id).UseSequence(""));
    }

    [Fact]
    public void UseSequence_WithNullScope_ThrowsArgumentNullException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            entityBuilder.Property(e => e.Id).UseSequence(null!, "order_seq"));
    }

    [Fact]
    public void UseSequence_WithEmptyScope_ThrowsArgumentException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            entityBuilder.Property(e => e.Id).UseSequence("", "order_seq"));
    }

    [Fact]
    public void UseSequence_ReturnsPropertyBuilder_ForChaining()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var propertyBuilder = entityBuilder.Property(e => e.Id);

        // Act
        var result = propertyBuilder.UseSequence("order_seq");

        // Assert
        Assert.Same(propertyBuilder, result);
    }

    [Fact]
    public void UseSequence_NonGeneric_SetsSequenceNameAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act
        entityBuilder.Property(nameof(TestEntity.Id)).UseSequence("order_seq");

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        Assert.Equal("order_seq", sequenceName);
    }

    [Fact]
    public void UseSequence_WithoutScope_ClearsPreviousScopeOverride()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act - First configure with scope, then override without scope
        entityBuilder.Property(e => e.Id)
            .UseSequence("custom_scope", "order_seq")
            .UseSequence("order_seq"); // Should clear the scope

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        var scopeName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value;
        
        Assert.Equal("order_seq", sequenceName);
        Assert.Null(scopeName); // Scope should be cleared
    }

    [Fact]
    public void UseSequence_NonGeneric_WithoutScope_ClearsPreviousScopeOverride()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act - First configure with scope, then override without scope
        entityBuilder.Property(nameof(TestEntity.Id))
            .UseSequence("custom_scope", "order_seq")
            .UseSequence("order_seq"); // Should clear the scope

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        var scopeName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value;
        
        Assert.Equal("order_seq", sequenceName);
        Assert.Null(scopeName); // Scope should be cleared
    }

    private class TestEntity
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }
}
