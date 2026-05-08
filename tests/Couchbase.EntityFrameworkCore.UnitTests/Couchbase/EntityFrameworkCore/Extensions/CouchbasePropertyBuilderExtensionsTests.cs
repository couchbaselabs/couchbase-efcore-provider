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

    [Fact]
    public void UseSequence_WithOptions_SetsOptionsAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5
        };

        // Act
        entityBuilder.Property(e => e.Id).UseSequence("order_seq", options);

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value as CouchbaseSequenceOptions;
        Assert.NotNull(storedOptions);
        Assert.Equal(100, storedOptions.StartWith);
        Assert.Equal(5, storedOptions.IncrementBy);
    }

    [Fact]
    public void UseSequence_WithoutOptions_ClearsPreviousOptionsAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5
        };

        // Act - First configure with options, then override without options
        entityBuilder.Property(e => e.Id)
            .UseSequence("order_seq", options)
            .UseSequence("order_seq"); // Should clear the options

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value;
        Assert.Null(storedOptions); // Options should be cleared
    }

    [Fact]
    public void UseSequence_WithScope_WithoutOptions_ClearsPreviousOptionsAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5
        };

        // Act - First configure with scope and options, then override with scope only
        entityBuilder.Property(e => e.Id)
            .UseSequence("analytics", "order_seq", options)
            .UseSequence("analytics", "order_seq"); // Should clear the options

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value;
        var scopeName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value;
        
        Assert.Null(storedOptions); // Options should be cleared
        Assert.Equal("analytics", scopeName); // Scope should still be set
    }

    [Fact]
    public void UseSequence_WithoutOptions_ClearsPreviousAutoCreateAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // First, manually set the auto-create annotation to simulate attribute processing
        entityBuilder.Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, "order_seq")
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation, false);

        // Act - Now call UseSequence without options, which should clear auto-create
        entityBuilder.Property(e => e.Id).UseSequence("order_seq");

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var autoCreate = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation)?.Value;
        Assert.Null(autoCreate); // Auto-create annotation should be cleared
    }

    [Fact]
    public void UseSequence_NonGeneric_WithoutOptions_ClearsPreviousOptionsAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5
        };

        // Act - First configure with options, then override without options using non-generic
        entityBuilder.Property(e => e.Id).UseSequence("order_seq", options);
        entityBuilder.Property(nameof(TestEntity.Id)).UseSequence("order_seq"); // Should clear the options

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value;
        Assert.Null(storedOptions); // Options should be cleared
    }

    [Fact]
    public void UseSequence_NonGeneric_WithScope_WithoutOptions_ClearsPreviousOptionsAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5
        };

        // Act - First configure with scope and options, then override with scope only
        entityBuilder.Property(e => e.Id).UseSequence("analytics", "order_seq", options);
        entityBuilder.Property(nameof(TestEntity.Id)).UseSequence("analytics", "order_seq"); // Should clear the options

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value;
        Assert.Null(storedOptions); // Options should be cleared
    }

    [Fact]
    public void UseSequence_ChainingFromOptionsToNoOptions_ClearsAllOverrides()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5
        };

        // Act - Configure with all options, then clear with basic overload
        entityBuilder.Property(e => e.Id)
            .UseSequence("custom_scope", "first_seq", options)
            .UseSequence("second_seq"); // Should clear scope, options, auto-create

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        var scopeName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value;
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value;
        var autoCreate = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation)?.Value;
        
        Assert.Equal("second_seq", sequenceName); // New sequence name
        Assert.Null(scopeName); // Scope should be cleared
        Assert.Null(storedOptions); // Options should be cleared
        Assert.Null(autoCreate); // Auto-create should be cleared
    }

    [Fact]
    public void UseSequence_WithScopeAndOptions_SetsBothAnnotations()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 500,
            IncrementBy = 10,
            Cycle = true
        };

        // Act
        entityBuilder.Property(e => e.Id).UseSequence("analytics", "order_seq", options);

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(TestEntity));
        var property = entityType!.FindProperty(nameof(TestEntity.Id));
        
        var sequenceName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value;
        var scopeName = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value;
        var storedOptions = property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value as CouchbaseSequenceOptions;
        
        Assert.Equal("order_seq", sequenceName);
        Assert.Equal("analytics", scopeName);
        Assert.NotNull(storedOptions);
        Assert.Equal(500, storedOptions.StartWith);
        Assert.Equal(10, storedOptions.IncrementBy);
        Assert.True(storedOptions.Cycle);
    }

    [Fact]
    public void UseSequence_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            entityBuilder.Property(e => e.Id).UseSequence("order_seq", (CouchbaseSequenceOptions)null!));
    }

    [Fact]
    public void UseSequence_WithScopeAndNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            entityBuilder.Property(e => e.Id).UseSequence("analytics", "order_seq", null!));
    }

    #region UseGuid Tests

    [Fact]
    public void UseGuid_SetsValueGeneratedOnAdd()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<GuidEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseGuid();

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(GuidEntity));
        var property = entityType!.FindProperty(nameof(GuidEntity.Id));

        Assert.Equal(ValueGenerated.OnAdd, property!.ValueGenerated);
    }

    [Fact]
    public void UseGuid_ClearsSequenceAnnotations()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<GuidEntity>();

        // First set a sequence, then switch to GUID
        entityBuilder.Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, "old_seq")
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation, "old_scope");

        // Act
        entityBuilder.Property(e => e.Id).UseGuid();

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(GuidEntity));
        var property = entityType!.FindProperty(nameof(GuidEntity.Id));

        Assert.Null(property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value);
        Assert.Null(property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value);
    }

    [Fact]
    public void UseGuidString_SetsFormatAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<StringIdEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseGuidString("N");

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(StringIdEntity));
        var property = entityType!.FindProperty(nameof(StringIdEntity.Id));

        var format = property!.FindAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation)?.Value;
        Assert.Equal("N", format);
    }

    [Fact]
    public void UseGuidString_DefaultFormat_UsesD()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<StringIdEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseGuidString();

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(StringIdEntity));
        var property = entityType!.FindProperty(nameof(StringIdEntity.Id));

        var format = property!.FindAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation)?.Value;
        Assert.Equal("D", format);
    }

    [Fact]
    public void UseGuidString_SetsValueGeneratedOnAdd()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<StringIdEntity>();

        // Act
        entityBuilder.Property(e => e.Id).UseGuidString();

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(StringIdEntity));
        var property = entityType!.FindProperty(nameof(StringIdEntity.Id));

        Assert.Equal(ValueGenerated.OnAdd, property!.ValueGenerated);
    }

    [Fact]
    public void UseGuidString_ClearsSequenceAnnotations()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<StringIdEntity>();

        // First set a sequence, then switch to GUID string
        entityBuilder.Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, "old_seq");

        // Act
        entityBuilder.Property(e => e.Id).UseGuidString();

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(StringIdEntity));
        var property = entityType!.FindProperty(nameof(StringIdEntity.Id));

        Assert.Null(property!.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value);
    }

    [Fact]
    public void UseGuid_NonGeneric_OnNonGuidProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<StringIdEntity>();

        // Act & Assert - Using non-generic Property() with string type should throw
        var exception = Assert.Throws<InvalidOperationException>(() =>
            entityBuilder.Property(nameof(StringIdEntity.Id)).UseGuid());

        Assert.Contains("UseGuid()", exception.Message);
        Assert.Contains("Guid", exception.Message);
        Assert.Contains("UseGuidString()", exception.Message);
    }

    [Fact]
    public void UseGuid_NonGeneric_OnIntProperty_ThrowsInvalidOperationException()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<TestEntity>();

        // Act & Assert - Using non-generic Property() with long type should throw
        var exception = Assert.Throws<InvalidOperationException>(() =>
            entityBuilder.Property(nameof(TestEntity.Id)).UseGuid());

        Assert.Contains("UseGuid()", exception.Message);
        Assert.Contains("Guid", exception.Message);
    }

    [Fact]
    public void UseGuid_ClearsGuidStringFormatAnnotation()
    {
        // Arrange
        var modelBuilder = new ModelBuilder();
        var entityBuilder = modelBuilder.Entity<GuidEntity>();

        // First set a GUID string format, then switch to plain GUID
        entityBuilder.Property(e => e.Id)
            .HasAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation, "N");

        // Act
        entityBuilder.Property(e => e.Id).UseGuid();

        // Assert
        var model = modelBuilder.Model;
        var entityType = model.FindEntityType(typeof(GuidEntity));
        var property = entityType!.FindProperty(nameof(GuidEntity.Id));

        Assert.Null(property!.FindAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation)?.Value);
    }

    #endregion

    private class TestEntity
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }

    private class GuidEntity
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
    }

    private class StringIdEntity
    {
        public string Id { get; set; } = null!;
        public string? Name { get; set; }
    }
}
