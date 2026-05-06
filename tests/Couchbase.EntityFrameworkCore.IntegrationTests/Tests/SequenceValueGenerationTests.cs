using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Integration tests for Couchbase sequence-based primary key value generation.
/// These tests verify the end-to-end flow from DI registration through SaveChangesAsync.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class SequenceValueGenerationTests : IAsyncLifetime
{
    private readonly BloggingFixture _fixture;
    private readonly ITestOutputHelper _outputHelper;
    private const string SequenceName = "test_entity_seq";

    public SequenceValueGenerationTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
    }

    public async Task InitializeAsync()
    {
        // Create collections if they don't exist
        await CreateCollectionIfNotExistsAsync("sequence_test_entities");
        await CreateCollectionIfNotExistsAsync("sequence_test_int_entities");
        
        // Create the sequence before running tests
        await CreateSequenceAsync();
    }

    public async Task DisposeAsync()
    {
        // Drop the sequence after tests complete
        await DropSequenceAsync();
    }

    private async Task CreateCollectionIfNotExistsAsync(string collectionName)
    {
        try
        {
            await using var context = CreateSequenceTestDbContext();
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE COLLECTION `{_fixture.BucketName}`.`{_fixture.ScopeName}`.`{collectionName}` IF NOT EXISTS";
            
            _outputHelper.WriteLine($"Creating collection: {command.CommandText}");
            await command.ExecuteNonQueryAsync();
            
            // Brief delay to allow collection to be ready
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _outputHelper.WriteLine($"Failed to create collection {collectionName}: {ex.Message}");
        }
    }

    private async Task CreateSequenceAsync()
    {
        try
        {
            await using var context = CreateSequenceTestDbContext();
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SEQUENCE `{_fixture.BucketName}`.`{_fixture.ScopeName}`.`{SequenceName}` START WITH 1 INCREMENT BY 1";
            
            _outputHelper.WriteLine($"Creating sequence: {command.CommandText}");
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _outputHelper.WriteLine($"Failed to create sequence (may already exist): {ex.Message}");
        }
    }

    private async Task DropSequenceAsync()
    {
        try
        {
            await using var context = CreateSequenceTestDbContext();
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP SEQUENCE `{_fixture.BucketName}`.`{_fixture.ScopeName}`.`{SequenceName}`";
            
            _outputHelper.WriteLine($"Dropping sequence: {command.CommandText}");
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _outputHelper.WriteLine($"Failed to drop sequence: {ex.Message}");
        }
    }

    private SequenceTestDbContext CreateSequenceTestDbContext()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<SequenceTestDbContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password)
                .WithLogging(loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = _fixture.BucketName;
                couchbaseDbContextOptions.Scope = _fixture.ScopeName;
            });
        optionsBuilder.UseCamelCaseNamingConvention();

        return new SequenceTestDbContext(optionsBuilder.Options, SequenceName);
    }

    /// <summary>
    /// End-to-end test proving the entire DI/model/save pipeline works:
    /// 1. CouchbaseValueGeneratorSelector is registered and resolved via DI
    /// 2. Property has correct sequence annotation from UseSequence()
    /// 3. SaveChangesAsync invokes the interceptor which calls the selector
    /// 4. Selector creates the correct CouchbaseSequenceValueGenerator
    /// 5. Generator executes sequence query and returns value
    /// 6. Value is assigned to entity before INSERT
    ///
    /// This test would fail if any part of the registration in
    /// CouchbaseServiceCollectionExtensions.cs line 62+ is broken.
    /// </summary>
    [Fact]
    public async Task EndToEnd_DIRegistration_SelectorUsedDuringSaveChanges()
    {
        // Arrange
        await using var context = CreateSequenceTestDbContext();

        // Step 1: Verify DI registration - IValueGeneratorSelector resolves to our implementation
        var selector = context.GetService<IValueGeneratorSelector>();
        Assert.NotNull(selector);
        Assert.IsType<CouchbaseValueGeneratorSelector>(selector);
        _outputHelper.WriteLine($"Step 1 PASS: IValueGeneratorSelector resolved to {selector.GetType().Name}");

        // Step 2: Verify model configuration - property has sequence annotation
        var entityType = context.Model.FindEntityType(typeof(SequenceTestEntity))!;
        var idProperty = entityType.FindProperty(nameof(SequenceTestEntity.Id))!;

        var sequenceAnnotation = idProperty.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation);
        Assert.NotNull(sequenceAnnotation);
        Assert.Equal(SequenceName, sequenceAnnotation.Value);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd, idProperty.ValueGenerated);
        _outputHelper.WriteLine($"Step 2 PASS: Property has annotation '{sequenceAnnotation.Value}' and ValueGenerated.OnAdd");

        // Step 3: Verify selector can create the generator for this property
        var generator = selector.Select(idProperty, entityType);
        Assert.NotNull(generator);
        Assert.IsType<CouchbaseSequenceValueGenerator<long>>(generator);
        _outputHelper.WriteLine($"Step 3 PASS: Selector created {generator.GetType().Name}");

        // Step 4: Create entity with default Id (0) and save
        var entity = new SequenceTestEntity { Name = "E2E Test Entity" };
        Assert.Equal(0, entity.Id); // Confirm Id starts at default
        _outputHelper.WriteLine($"Step 4: Entity created with Id={entity.Id}");

        context.SequenceTestEntities.Add(entity);

        // Step 5: SaveChangesAsync should trigger interceptor -> selector -> generator -> sequence query
        await context.SaveChangesAsync();

        // Step 6: Verify Id was assigned a positive value from the sequence
        Assert.NotEqual(0, entity.Id);
        Assert.True(entity.Id > 0, $"Expected positive Id from sequence, got {entity.Id}");
        _outputHelper.WriteLine($"Step 5-6 PASS: After SaveChangesAsync, entity.Id={entity.Id}");

        // Cleanup
        context.SequenceTestEntities.Remove(entity);
        await context.SaveChangesAsync();

        _outputHelper.WriteLine("END-TO-END TEST PASSED: Full DI -> Model -> Save -> Sequence pipeline verified");
    }

    [Fact]
    public async Task Diagnostic_VerifyModelConfiguration()
    {
        // Diagnostic test to verify the model is configured correctly
        await using var context = CreateSequenceTestDbContext();

        var entityType = context.Model.FindEntityType(typeof(SequenceTestEntity));
        Assert.NotNull(entityType);

        var idProperty = entityType.FindProperty(nameof(SequenceTestEntity.Id));
        Assert.NotNull(idProperty);

        // Check ValueGenerated
        _outputHelper.WriteLine($"ValueGenerated: {idProperty.ValueGenerated}");
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd, idProperty.ValueGenerated);

        // Check for sequence annotation
        var sequenceAnnotation = idProperty.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation);
        _outputHelper.WriteLine($"SequenceAnnotation: {sequenceAnnotation?.Value}");
        Assert.NotNull(sequenceAnnotation);
        Assert.Equal(SequenceName, sequenceAnnotation.Value);

        // Check that the value generator selector is our implementation
        var selector = context.GetService<IValueGeneratorSelector>();
        _outputHelper.WriteLine($"Selector type: {selector?.GetType().FullName}");
        Assert.IsType<CouchbaseValueGeneratorSelector>(selector);
    }

    [Fact]
    public async Task SaveChangesAsync_WithSequenceValueGeneration_AssignsGeneratedId()
    {
        // Arrange
        await using var context = CreateSequenceTestDbContext();
        var entity = new SequenceTestEntity
        {
            Name = "Test Entity"
        };

        // Act - Add entity with Id = 0 (default), SaveChanges should fetch sequence value
        context.SequenceTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Assert - Id should have been assigned from sequence
        Assert.NotEqual(0, entity.Id);
        _outputHelper.WriteLine($"Generated Id: {entity.Id}");

        // Cleanup
        context.SequenceTestEntities.Remove(entity);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithMultipleEntities_AssignsUniqueIds()
    {
        // Arrange
        await using var context = CreateSequenceTestDbContext();
        var entities = new[]
        {
            new SequenceTestEntity { Name = "Entity 1" },
            new SequenceTestEntity { Name = "Entity 2" },
            new SequenceTestEntity { Name = "Entity 3" }
        };

        // Act
        context.SequenceTestEntities.AddRange(entities);
        await context.SaveChangesAsync();

        // Assert - All entities should have unique, non-zero IDs
        var ids = entities.Select(e => e.Id).ToList();
        Assert.All(ids, id => Assert.NotEqual(0, id));
        Assert.Equal(ids.Count, ids.Distinct().Count()); // All unique

        _outputHelper.WriteLine($"Generated Ids: {string.Join(", ", ids)}");

        // Cleanup
        context.SequenceTestEntities.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithIntProperty_AssignsGeneratedId()
    {
        // Arrange - Test with int (not long) to verify type conversion works
        await using var context = CreateSequenceTestDbContext();
        var entity = new SequenceTestEntityWithIntId
        {
            Name = "Int Id Entity"
        };

        // Act
        context.IntIdEntities.Add(entity);
        await context.SaveChangesAsync();

        // Assert
        Assert.NotEqual(0, entity.Id);
        Assert.IsType<int>(entity.Id);
        _outputHelper.WriteLine($"Generated int Id: {entity.Id}");

        // Cleanup
        context.IntIdEntities.Remove(entity);
        await context.SaveChangesAsync();
    }

    [Fact]  
    public async Task SaveChangesAsync_WithCancellation_ThrowsException()
    {
        // Arrange
        await using var context = CreateSequenceTestDbContext();
        var entity = new SequenceTestEntity { Name = "Cancel Test" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // Cancellation may result in OperationCanceledException, TaskCanceledException, 
        // or a Couchbase-specific exception like AmbiguousTimeoutException
        context.SequenceTestEntities.Add(entity);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => 
            context.SaveChangesAsync(cts.Token));
        
        // Verify it's either a cancellation-related exception or a timeout
        Assert.True(
            exception is OperationCanceledException || 
            exception is TaskCanceledException ||
            exception.GetType().Name.Contains("Timeout") ||
            exception.GetType().Name.Contains("Cancel"),
            $"Expected cancellation-related exception but got: {exception.GetType().Name}");
    }

    [Fact]
    public async Task UseSequence_WithOptions_SetsAnnotationCorrectly()
    {
        // Arrange
        await using var context = CreateSequenceTestDbContext();

        // Create a test context with options
        var optionsBuilder = new DbContextOptionsBuilder<AutoCreateSequenceDbContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = _fixture.BucketName;
                couchbaseDbContextOptions.Scope = _fixture.ScopeName;
            });

        await using var testContext = new AutoCreateSequenceDbContext(optionsBuilder.Options);

        // Act
        var entityType = testContext.Model.FindEntityType(typeof(AutoCreateSequenceEntity))!;
        var idProperty = entityType.FindProperty(nameof(AutoCreateSequenceEntity.Id))!;

        // Assert
        var sequenceAnnotation = idProperty.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation);
        Assert.NotNull(sequenceAnnotation);
        Assert.Equal("auto_seq", sequenceAnnotation.Value);

        var optionsAnnotation = idProperty.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation);
        Assert.NotNull(optionsAnnotation);

        var options = optionsAnnotation.Value as CouchbaseSequenceOptions;
        Assert.NotNull(options);
        Assert.Equal(1000, options.StartWith);
        Assert.Equal(10, options.IncrementBy);

        _outputHelper.WriteLine($"Sequence options: StartWith={options.StartWith}, IncrementBy={options.IncrementBy}");
    }

    /// <summary>
    /// Test entity with long Id using sequence value generation via fluent API.
    /// </summary>
    public class SequenceTestEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Test entity for auto-create sequence tests.
    /// </summary>
    public class AutoCreateSequenceEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// DbContext for testing sequence auto-creation with options.
    /// </summary>
    public class AutoCreateSequenceDbContext : DbContext
    {
        public AutoCreateSequenceDbContext(DbContextOptions<AutoCreateSequenceDbContext> options)
            : base(options)
        {
        }

        public DbSet<AutoCreateSequenceEntity> AutoCreateEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AutoCreateSequenceEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).UseSequence("auto_seq", new CouchbaseSequenceOptions
                {
                    StartWith = 1000,
                    IncrementBy = 10
                });
            });
        }
    }

    /// <summary>
    /// Test entity with int Id to verify type conversion.
    /// </summary>
    public class SequenceTestEntityWithIntId
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// DbContext for sequence value generation tests.
    /// </summary>
    public class SequenceTestDbContext : DbContext
    {
        private readonly string _sequenceName;

        public SequenceTestDbContext(DbContextOptions<SequenceTestDbContext> options, string sequenceName)
            : base(options)
        {
            _sequenceName = sequenceName;
        }

        public DbSet<SequenceTestEntity> SequenceTestEntities { get; set; }
        public DbSet<SequenceTestEntityWithIntId> IntIdEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure sequence value generation via fluent API
            modelBuilder.Entity<SequenceTestEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).UseSequence(_sequenceName);
                entity.ToCouchbaseCollection(this, "sequence_test_entities");
            });

            modelBuilder.Entity<SequenceTestEntityWithIntId>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).UseSequence(_sequenceName);
                entity.ToCouchbaseCollection(this, "sequence_test_int_entities");
            });

            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }
}
