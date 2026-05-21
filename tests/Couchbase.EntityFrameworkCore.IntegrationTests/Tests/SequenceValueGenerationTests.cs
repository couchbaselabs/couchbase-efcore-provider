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
    /// Integration test verifying that EnsureCreatedAsync auto-creates sequences.
    /// Tests the full lifecycle:
    /// 1. Drop sequence if exists (clean slate)
    /// 2. Call EnsureCreatedAsync (should create sequence with specified options)
    /// 3. Verify sequence works via NEXT VALUE FOR
    /// 4. Verify sequence options were applied (StartWith, IncrementBy)
    /// </summary>
    [Fact]
    public async Task EnsureCreatedAsync_AutoCreatesSequence_WithOptions()
    {
        const string autoCreateSeqName = "ensure_created_test_seq";

        // Step 1: Clean up any existing sequence
        await DropSequenceIfExistsAsync(autoCreateSeqName);
        _outputHelper.WriteLine("Step 1: Dropped sequence if existed");

        // Step 2: Create context with auto-create sequence configuration
        var optionsBuilder = new DbContextOptionsBuilder<EnsureCreatedSequenceDbContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = _fixture.BucketName;
                couchbaseDbContextOptions.Scope = _fixture.ScopeName;
            });

        await using var context = new EnsureCreatedSequenceDbContext(optionsBuilder.Options);

        // Step 3: Call EnsureCreatedAsync - this should create the sequence
        await context.Database.EnsureCreatedAsync();
        _outputHelper.WriteLine("Step 2: EnsureCreatedAsync completed");

        // Step 4: Verify sequence was created by fetching next value
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT NEXT VALUE FOR `{_fixture.BucketName}`.`{_fixture.ScopeName}`.`{autoCreateSeqName}`";
        _outputHelper.WriteLine($"Step 3: Executing query: {command.CommandText}");

        var result = await command.ExecuteScalarAsync();
        Assert.NotNull(result);
        var firstValue = Convert.ToInt64(result);
        _outputHelper.WriteLine($"Step 3: First sequence value = {firstValue}");

        // Verify StartWith option was applied (should start at 100)
        Assert.Equal(100, firstValue);

        // Step 5: Fetch another value to verify IncrementBy
        result = await command.ExecuteScalarAsync();
        var secondValue = Convert.ToInt64(result);
        _outputHelper.WriteLine($"Step 4: Second sequence value = {secondValue}");

        // Verify IncrementBy option was applied (should increment by 5)
        Assert.Equal(105, secondValue);

        // Cleanup
        await DropSequenceIfExistsAsync(autoCreateSeqName);
        _outputHelper.WriteLine("Cleanup: Dropped test sequence");

        _outputHelper.WriteLine("TEST PASSED: EnsureCreatedAsync auto-created sequence with correct options");
    }

    /// <summary>
    /// Integration test verifying that SaveChangesAsync works with auto-created sequences.
    /// Full end-to-end: EnsureCreatedAsync creates sequence, then SaveChanges uses it.
    /// </summary>
    [Fact]
    public async Task EnsureCreatedAsync_ThenSaveChanges_UsesAutoCreatedSequence()
    {
        const string autoCreateSeqName = "e2e_auto_seq";

        // Clean up
        await DropSequenceIfExistsAsync(autoCreateSeqName);

        var optionsBuilder = new DbContextOptionsBuilder<E2EAutoCreateSequenceDbContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = _fixture.BucketName;
                couchbaseDbContextOptions.Scope = _fixture.ScopeName;
            });

        await using var context = new E2EAutoCreateSequenceDbContext(optionsBuilder.Options);

        // Create collection and sequence via EnsureCreatedAsync
        await context.Database.EnsureCreatedAsync();
        _outputHelper.WriteLine("EnsureCreatedAsync completed");

        // Create entity with default Id (0)
        var entity = new E2EAutoCreateEntity { Name = "Auto-created sequence test" };
        Assert.Equal(0, entity.Id);

        // Add and save - should use the auto-created sequence
        context.Entities.Add(entity);
        await context.SaveChangesAsync();

        // Verify Id was assigned from sequence (starts at 1)
        Assert.True(entity.Id > 0, $"Expected positive Id from sequence, got {entity.Id}");
        _outputHelper.WriteLine($"Entity saved with Id = {entity.Id}");

        // Cleanup
        context.Entities.Remove(entity);
        await context.SaveChangesAsync();
        await DropSequenceIfExistsAsync(autoCreateSeqName);

        _outputHelper.WriteLine("TEST PASSED: End-to-end auto-create sequence workflow verified");
    }

    private async Task DropSequenceIfExistsAsync(string sequenceName)
    {
        try
        {
            await using var context = CreateSequenceTestDbContext();
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP SEQUENCE IF EXISTS `{_fixture.BucketName}`.`{_fixture.ScopeName}`.`{sequenceName}`";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _outputHelper.WriteLine($"Note: Could not drop sequence {sequenceName}: {ex.Message}");
        }
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

    /// <summary>
    /// Entity for EnsureCreatedAsync sequence auto-creation test.
    /// </summary>
    public class EnsureCreatedSequenceEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// DbContext for testing EnsureCreatedAsync sequence auto-creation with custom options.
    /// </summary>
    public class EnsureCreatedSequenceDbContext : DbContext
    {
        public EnsureCreatedSequenceDbContext(DbContextOptions<EnsureCreatedSequenceDbContext> options)
            : base(options)
        {
        }

        public DbSet<EnsureCreatedSequenceEntity> Entities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EnsureCreatedSequenceEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                // Configure sequence with specific options to verify they're applied
                entity.Property(e => e.Id).UseSequence("ensure_created_test_seq", new CouchbaseSequenceOptions
                {
                    StartWith = 100,
                    IncrementBy = 5
                });
                entity.ToCouchbaseCollection(this, "ensure_created_test_entities");
            });

            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }

    /// <summary>
    /// Entity for end-to-end auto-create sequence test.
    /// </summary>
    public class E2EAutoCreateEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// DbContext for end-to-end auto-create sequence test.
    /// </summary>
    public class E2EAutoCreateSequenceDbContext : DbContext
    {
        public E2EAutoCreateSequenceDbContext(DbContextOptions<E2EAutoCreateSequenceDbContext> options)
            : base(options)
        {
        }

        public DbSet<E2EAutoCreateEntity> Entities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<E2EAutoCreateEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                // Default options (StartWith = 1, IncrementBy = 1)
                entity.Property(e => e.Id).UseSequence("e2e_auto_seq");
                entity.ToCouchbaseCollection(this, "e2e_auto_create_entities");
            });

            modelBuilder.ConfigureToCouchbase(this, true);
        }
    }
}
