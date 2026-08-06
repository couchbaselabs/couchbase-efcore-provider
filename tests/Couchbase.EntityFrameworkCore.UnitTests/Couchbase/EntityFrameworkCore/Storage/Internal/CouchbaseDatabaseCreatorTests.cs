using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.UnitTests.Fakes;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseCreatorTests
{
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IDesignTimeModel> _mockDesignTimeModel;
    private readonly Mock<ILogger<CouchbaseDatabaseCreator>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ICouchbaseDbContextOptionsBuilder> _mockOptions;
    private readonly Mock<ISqlGenerationHelper> _mockSqlGenerationHelper;
    private readonly Mock<IClusterProvider> _mockClusterProvider;
    private readonly Mock<ICluster> _mockCluster;
    private readonly Mock<IBucket> _mockBucket;
    private readonly Mock<IScope> _mockScope;
    private readonly Mock<ICouchbaseCollectionManager> _mockCollectionManager;
    private readonly Mock<IBucketManager> _mockBucketManager;
    private readonly Mock<IModel> _mockModel;

    public CouchbaseDatabaseCreatorTests()
    {
        _mockDatabase = new Mock<IDatabase>();
        _mockDesignTimeModel = new Mock<IDesignTimeModel>();
        _mockLogger = new Mock<ILogger<CouchbaseDatabaseCreator>>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockOptions = new Mock<ICouchbaseDbContextOptionsBuilder>();
        _mockSqlGenerationHelper = new Mock<ISqlGenerationHelper>();
        _mockClusterProvider = new Mock<IClusterProvider>();
        _mockCluster = new Mock<ICluster>();
        _mockBucket = new Mock<IBucket>();
        _mockScope = new Mock<IScope>();
        _mockCollectionManager = new Mock<ICouchbaseCollectionManager>();
        _mockBucketManager = new Mock<IBucketManager>();
        _mockModel = new Mock<IModel>();

        // Default setup
        _mockOptions.Setup(o => o.Bucket).Returns("test-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("test-scope");
        _mockOptions.Setup(o => o.AutoCreateScopes).Returns(false);
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(false);

        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IClusterProvider)))
            .Returns(_mockClusterProvider.Object);
        _mockClusterProvider.Setup(cp => cp.GetClusterAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockCluster.Object);
        _mockCluster.Setup(c => c.BucketAsync("test-bucket"))
            .ReturnsAsync(_mockBucket.Object);
        _mockCluster.Setup(c => c.Buckets).Returns(_mockBucketManager.Object);
        _mockBucket.Setup(b => b.Collections).Returns(_mockCollectionManager.Object);
        _mockBucket.Setup(b => b.ScopeAsync(It.IsAny<string>())).ReturnsAsync(_mockScope.Object);
        _mockScope.Setup(s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(CreateFakeQueryResult(new List<dynamic>()));
        _mockCluster.Setup(c => c.QueryAsync<int>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(CreateFakeQueryResult(new List<int> { 1 }));

        _mockDesignTimeModel.Setup(m => m.Model).Returns(_mockModel.Object);
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(Array.Empty<IEntityType>());

        _mockSqlGenerationHelper.Setup(h => h.DelimitIdentifier(It.IsAny<string>()))
            .Returns<string>(s => $"`{s}`");
    }

    private CouchbaseDatabaseCreator CreateCreator(TimeProvider? timeProvider = null)
    {
        // Create minimal dependencies - we'll use reflection or make methods testable
        var dependencies = CreateMockDependencies();
        return new CouchbaseDatabaseCreator(
            dependencies,
            _mockDatabase.Object,
            _mockServiceProvider.Object,
            _mockDesignTimeModel.Object,
            _mockLogger.Object,
            _mockOptions.Object,
            _mockSqlGenerationHelper.Object,
            timeProvider);
    }

    private static IQueryResult<T> CreateFakeQueryResult<T>(List<T> rows)
    {
        return new FakeQueryResult<T> { Rows = rows.ToAsyncEnumerable() };
    }

    private static Mock<IProperty> CreateMockProperty(string columnName, IEntityType declaringEntityType)
    {
        var mockProperty = new Mock<IProperty>();
        var columnNameAnnotation = new Mock<IAnnotation>();
        columnNameAnnotation.Setup(a => a.Value).Returns(columnName);
        mockProperty.Setup(p => p.FindAnnotation(RelationalAnnotationNames.ColumnName)).Returns(columnNameAnnotation.Object);
        mockProperty.Setup(p => p.Name).Returns(columnName);
        mockProperty.Setup(p => p.DeclaringType).Returns(declaringEntityType);
        return mockProperty;
    }

    private static Mock<IIndex> CreateMockIndex(
        IEntityType declaringEntityType,
        IReadOnlyList<IProperty> properties,
        string indexName,
        bool isUnique = false,
        string? filter = null)
    {
        var mockIndex = new Mock<IIndex>();
        mockIndex.Setup(i => i.Properties).Returns(properties);
        mockIndex.Setup(i => i.IsUnique).Returns(isUnique);
        mockIndex.Setup(i => i.DeclaringEntityType).Returns(declaringEntityType);
        // IIndex.DeclaringEntityType is a covariant `new` property hiding the base
        // IReadOnlyIndex.DeclaringEntityType slot -- GetDatabaseName()/GetFilter() etc. are
        // extension methods on IReadOnlyIndex, so that base slot must be set up separately or it
        // resolves to Moq's default (null), NullReferenceException-ing downstream.
        mockIndex.As<IReadOnlyIndex>().Setup(i => i.DeclaringEntityType).Returns(declaringEntityType);
        mockIndex.Setup(i => i.Name).Returns((string?)null);
        mockIndex.Setup(i => i[RelationalAnnotationNames.Name]).Returns(indexName);

        if (filter != null)
        {
            var filterAnnotation = new Mock<IAnnotation>();
            filterAnnotation.Setup(a => a.Value).Returns(filter);
            mockIndex.Setup(i => i.FindAnnotation(RelationalAnnotationNames.Filter)).Returns(filterAnnotation.Object);
        }

        return mockIndex;
    }

    private RelationalDatabaseCreatorDependencies CreateMockDependencies()
    {
        var mockConnection = new Mock<IRelationalConnection>();
        var mockModelDiffer = new Mock<IMigrationsModelDiffer>();
        var mockMigrationsSqlGenerator = new Mock<IMigrationsSqlGenerator>();
        var mockMigrationCommandExecutor = new Mock<IMigrationCommandExecutor>();
        var mockSqlGenerationHelper = new Mock<ISqlGenerationHelper>();
        var mockCurrentContext = new Mock<ICurrentDbContext>();
        var mockModel = new Mock<IModel>();
        var mockDbContextOptions = new Mock<IDbContextOptions>();
        var mockCommandLogger = new Mock<IRelationalCommandDiagnosticsLogger>();
        var mockExceptionDetector = new Mock<IExceptionDetector>();

        var mockDbContext = new Mock<DbContext>();
        mockCurrentContext.Setup(c => c.Context).Returns(mockDbContext.Object);

        return new RelationalDatabaseCreatorDependencies(
            mockConnection.Object,
            mockModelDiffer.Object,
            mockMigrationsSqlGenerator.Object,
            mockMigrationCommandExecutor.Object,
            mockSqlGenerationHelper.Object,
            Mock.Of<IExecutionStrategy>(),
            mockCurrentContext.Object,
            mockDbContextOptions.Object,
            mockCommandLogger.Object,
            mockExceptionDetector.Object);
    }

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WhenBucketExists_ReturnsTrue()
    {
        // Arrange
        _mockBucketManager.Setup(m => m.GetBucketAsync("test-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "test-bucket" });
        var creator = CreateCreator();

        // Act
        var result = await creator.ExistsAsync();

        // Assert
        Assert.True(result);
        _mockBucketManager.Verify(m => m.GetBucketAsync("test-bucket", It.IsAny<GetBucketOptions>()), Times.Once);
    }

    [Fact]
    public async Task ExistsAsync_WhenBucketNotFound_ReturnsFalse()
    {
        // Arrange
        _mockBucketManager.Setup(m => m.GetBucketAsync("test-bucket", It.IsAny<GetBucketOptions>()))
            .ThrowsAsync(new BucketNotFoundException("test-bucket"));
        var creator = CreateCreator();

        // Act
        var result = await creator.ExistsAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_UsesBucketNameNotScopeName()
    {
        // Arrange - bucket and scope have different names
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        var creator = CreateCreator();

        // Act
        await creator.ExistsAsync();

        // Assert - should check for bucket, not scope
        _mockBucketManager.Verify(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()), Times.Once);
        _mockBucketManager.Verify(m => m.GetBucketAsync("my-scope", It.IsAny<GetBucketOptions>()), Times.Never);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_CreatesBucketWithCorrectName()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("new-bucket");
        BucketSettings? capturedSettings = null;
        _mockBucketManager.Setup(m => m.CreateBucketAsync(It.IsAny<BucketSettings>(), It.IsAny<CreateBucketOptions>()))
            .Callback<BucketSettings, CreateBucketOptions>((s, _) => capturedSettings = s)
            .Returns(Task.CompletedTask);
        var creator = CreateCreator();

        // Act
        await creator.CreateAsync();

        // Assert
        Assert.NotNull(capturedSettings);
        Assert.Equal("new-bucket", capturedSettings.Name);
    }

    [Fact]
    public async Task CreateAsync_UsesBucketNameNotScopeName()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("correct-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("some-scope");
        BucketSettings? capturedSettings = null;
        _mockBucketManager.Setup(m => m.CreateBucketAsync(It.IsAny<BucketSettings>(), It.IsAny<CreateBucketOptions>()))
            .Callback<BucketSettings, CreateBucketOptions>((s, _) => capturedSettings = s)
            .Returns(Task.CompletedTask);
        var creator = CreateCreator();

        // Act
        await creator.CreateAsync();

        // Assert
        Assert.NotNull(capturedSettings);
        Assert.Equal("correct-bucket", capturedSettings.Name);
        Assert.NotEqual("some-scope", capturedSettings.Name);
    }

    [Fact]
    public async Task CreateAsync_WhenBucketExists_DoesNotThrow()
    {
        // Arrange
        _mockBucketManager.Setup(m => m.CreateBucketAsync(It.IsAny<BucketSettings>(), It.IsAny<CreateBucketOptions>()))
            .ThrowsAsync(new BucketExistsException("test-bucket"));
        var creator = CreateCreator();

        // Act & Assert - should not throw
        await creator.CreateAsync();
    }

    #endregion

    #region EnsureCreatedAsync Tests - Scope Creation

    [Fact]
    public async Task EnsureCreatedAsync_ChecksForCorrectScopeNotBucket()
    {
        // Arrange - This test would have caught the original bug
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        // Bucket exists
        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        // Setup bucket retrieval for scope operations
        _mockCluster.Setup(c => c.BucketAsync("my-bucket"))
            .ReturnsAsync(_mockBucket.Object);

        // Return scopes that include "my-bucket" but NOT "my-scope"
        // If the bug existed (checking Bucket instead of Scope), scope creation would be skipped
        var existingScopes = new List<ScopeSpec>
        {
            new ScopeSpec("my-bucket"), // Bucket name exists as a scope (edge case)
            new ScopeSpec("_default")
        };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - should attempt to create "my-scope", not skip because "my-bucket" scope exists
        _mockCollectionManager.Verify(
            m => m.CreateScopeAsync("my-scope", It.IsAny<CreateScopeOptions>()),
            Times.Once,
            "Should create scope using Scope name, not Bucket name");
        _mockCollectionManager.Verify(
            m => m.CreateScopeAsync("my-bucket", It.IsAny<CreateScopeOptions>()),
            Times.Never,
            "Should not try to create scope using Bucket name");
    }

    [Fact]
    public async Task EnsureCreatedAsync_WhenScopeExists_DoesNotCreateScope()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        _mockCluster.Setup(c => c.BucketAsync("my-bucket"))
            .ReturnsAsync(_mockBucket.Object);

        // Scope already exists
        var existingScopes = new List<ScopeSpec>
        {
            new ScopeSpec("my-scope"),
            new ScopeSpec("_default")
        };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - should not attempt to create scope since it exists
        _mockCollectionManager.Verify(
            m => m.CreateScopeAsync(It.IsAny<string>(), It.IsAny<CreateScopeOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WhenScopeDoesNotExist_CreatesScope()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("new-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        _mockCluster.Setup(c => c.BucketAsync("my-bucket"))
            .ReturnsAsync(_mockBucket.Object);

        // Scope does not exist
        var existingScopes = new List<ScopeSpec>
        {
            new ScopeSpec("_default")
        };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert
        _mockCollectionManager.Verify(
            m => m.CreateScopeAsync("new-scope", It.IsAny<CreateScopeOptions>()),
            Times.Once);
    }

    #endregion

    #region EnsureCreatedAsync Tests - Collection Creation

    [Fact]
    public async Task EnsureCreatedAsync_CreatesCollectionsInCorrectScope()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        _mockCluster.Setup(c => c.BucketAsync("my-bucket"))
            .ReturnsAsync(_mockBucket.Object);

        var existingScopes = new List<ScopeSpec> { new ScopeSpec("my-scope") };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        // Setup entity type using annotation (GetTableName is an extension method that reads this)
        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - collection should be created in the configured scope
        _mockCollectionManager.Verify(
            m => m.CreateCollectionAsync("my-scope", "TestCollection", It.IsAny<CreateCollectionSettings>(), It.IsAny<CreateCollectionOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_SecondaryBucketDifferentScope_DoesNotCreateConfiguredScopeThere()
    {
        // A single context maps an entity to a non-configured scope in a secondary bucket, with
        // AutoCreateScopes disabled. The configured scope must NOT be created in that secondary
        // bucket — nothing will be stored there, so creating it is unnecessary and can trip
        // permission failures.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateScopes).Returns(false);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        // Configured bucket: the configured scope already exists.
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        // Secondary bucket with its own collection manager, exposing only the default scope.
        var mockSecondaryBucket = new Mock<IBucket>();
        var mockSecondaryCollectionManager = new Mock<ICouchbaseCollectionManager>();
        mockSecondaryBucket.Setup(b => b.Collections).Returns(mockSecondaryCollectionManager.Object);
        mockSecondaryCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("_default") });
        _mockCluster.Setup(c => c.BucketAsync("secondary")).ReturnsAsync(mockSecondaryBucket.Object);

        // Entity lives in secondary.other-scope.OtherCollection.
        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("secondary.other-scope.OtherCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - nothing is created in the secondary bucket: not the configured scope, not the
        // non-default scope (AutoCreateScopes off), and not the collection.
        mockSecondaryCollectionManager.Verify(
            m => m.CreateScopeAsync("my-scope", It.IsAny<CreateScopeOptions>()), Times.Never);
        mockSecondaryCollectionManager.Verify(
            m => m.CreateScopeAsync("other-scope", It.IsAny<CreateScopeOptions>()), Times.Never);
        mockSecondaryCollectionManager.Verify(
            m => m.CreateCollectionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CreateCollectionSettings>(), It.IsAny<CreateCollectionOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateScopes_CreatesNonDefaultScopes()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("default-scope");
        _mockOptions.Setup(o => o.AutoCreateScopes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        _mockCluster.Setup(c => c.BucketAsync("my-bucket"))
            .ReturnsAsync(_mockBucket.Object);

        var existingScopes = new List<ScopeSpec> { new ScopeSpec("default-scope") };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        // Entity mapped to non-default scope via keyspace annotation
        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("my-bucket.other-scope.OtherCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - should create the non-default scope
        _mockCollectionManager.Verify(
            m => m.CreateScopeAsync("other-scope", It.IsAny<CreateScopeOptions>()),
            Times.Once);
        _mockCollectionManager.Verify(
            m => m.CreateCollectionAsync("other-scope", "OtherCollection", It.IsAny<CreateCollectionSettings>(), It.IsAny<CreateCollectionOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithoutAutoCreateScopes_SkipsNonDefaultScopeCollections()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("default-scope");
        _mockOptions.Setup(o => o.AutoCreateScopes).Returns(false);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });

        _mockCluster.Setup(c => c.BucketAsync("my-bucket"))
            .ReturnsAsync(_mockBucket.Object);

        var existingScopes = new List<ScopeSpec> { new ScopeSpec("default-scope") };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        // Entity mapped to non-default scope
        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("my-bucket.other-scope.OtherCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - should NOT create the non-default scope or collection
        _mockCollectionManager.Verify(
            m => m.CreateScopeAsync("other-scope", It.IsAny<CreateScopeOptions>()),
            Times.Never);
        _mockCollectionManager.Verify(
            m => m.CreateCollectionAsync("other-scope", It.IsAny<string>(), It.IsAny<CreateCollectionSettings>(), It.IsAny<CreateCollectionOptions>()),
            Times.Never);
    }

    #endregion

    #region EnsureCreatedAsync Tests - Sequence Creation

    private static Mock<IProperty> CreateMockSequenceProperty(IEntityType declaringEntityType, string sequenceName)
    {
        var mockProperty = new Mock<IProperty>();
        var sequenceNameAnnotation = new Mock<IAnnotation>();
        sequenceNameAnnotation.Setup(a => a.Value).Returns(sequenceName);
        mockProperty.Setup(p => p.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation))
            .Returns(sequenceNameAnnotation.Object);
        mockProperty.Setup(p => p.DeclaringType).Returns(declaringEntityType);
        mockProperty.Setup(p => p.Name).Returns("Id");
        return mockProperty;
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithSequenceOnEntityMappedToDifferentBucket_CreatesSequenceInEntityMappedBucket()
    {
        // Arrange - the entity lives in "secondary-bucket", not the context's configured bucket
        // ("my-bucket"). The sequence backing one of its properties must be created in
        // "secondary-bucket" too, not the configured bucket -- this is the cross-bucket sequences
        // fix (previously CreateSequencesAsync always used the configured bucket).
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        // Secondary bucket with its own collection manager + scope, mirroring
        // EnsureCreatedAsync_SecondaryBucketDifferentScope_DoesNotCreateConfiguredScopeThere's setup.
        var mockSecondaryBucket = new Mock<IBucket>();
        var mockSecondaryCollectionManager = new Mock<ICouchbaseCollectionManager>();
        var mockSecondaryScope = new Mock<IScope>();
        mockSecondaryBucket.Setup(b => b.Collections).Returns(mockSecondaryCollectionManager.Object);
        mockSecondaryBucket.Setup(b => b.ScopeAsync("my-scope")).ReturnsAsync(mockSecondaryScope.Object);
        mockSecondaryCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });
        mockSecondaryScope.Setup(s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(CreateFakeQueryResult(new List<dynamic>()));
        _mockCluster.Setup(c => c.BucketAsync("secondary-bucket")).ReturnsAsync(mockSecondaryBucket.Object);

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("secondary-bucket.my-scope.MyCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));

        var mockSequenceProperty = CreateMockSequenceProperty(mockEntityType.Object, "order_seq");
        mockEntityType.Setup(e => e.GetProperties()).Returns(new[] { mockSequenceProperty.Object });
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - the sequence is created in the entity's actual bucket...
        mockSecondaryScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE SEQUENCE IF NOT EXISTS")
                                      && sql.Contains("`secondary-bucket`") && sql.Contains("`my-scope`")
                                      && sql.Contains("`order_seq`")),
                It.IsAny<QueryOptions>()),
            Times.Once);

        // ...and not in the configured bucket.
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(It.Is<string>(sql => sql.StartsWith("CREATE SEQUENCE")), It.IsAny<QueryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_SameSequenceNameAndScopeInDifferentBuckets_CreatesBothNotAConflict()
    {
        // Arrange - two entities in DIFFERENT buckets happen to use the same sequence name in the
        // same (default) scope. A sequence's true identity is bucket.scope.name, so this must
        // create two distinct sequences, not be treated as a name conflict.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockSecondaryBucket = new Mock<IBucket>();
        var mockSecondaryCollectionManager = new Mock<ICouchbaseCollectionManager>();
        var mockSecondaryScope = new Mock<IScope>();
        mockSecondaryBucket.Setup(b => b.Collections).Returns(mockSecondaryCollectionManager.Object);
        mockSecondaryBucket.Setup(b => b.ScopeAsync("my-scope")).ReturnsAsync(mockSecondaryScope.Object);
        mockSecondaryCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });
        mockSecondaryScope.Setup(s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(CreateFakeQueryResult(new List<dynamic>()));
        _mockCluster.Setup(c => c.BucketAsync("secondary-bucket")).ReturnsAsync(mockSecondaryBucket.Object);

        var mockEntityTypeA = new Mock<IEntityType>();
        var mockTableNameAnnotationA = new Mock<IAnnotation>();
        mockTableNameAnnotationA.Setup(a => a.Value).Returns("CollectionA");
        mockEntityTypeA.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotationA.Object);
        mockEntityTypeA.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        var mockSequencePropertyA = CreateMockSequenceProperty(mockEntityTypeA.Object, "shared_seq");
        mockEntityTypeA.Setup(e => e.GetProperties()).Returns(new[] { mockSequencePropertyA.Object });

        var mockEntityTypeB = new Mock<IEntityType>();
        var mockTableNameAnnotationB = new Mock<IAnnotation>();
        mockTableNameAnnotationB.Setup(a => a.Value).Returns("secondary-bucket.my-scope.CollectionB");
        mockEntityTypeB.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotationB.Object);
        mockEntityTypeB.Setup(e => e.ClrType).Returns(typeof(TestDerivedEntity));
        var mockSequencePropertyB = CreateMockSequenceProperty(mockEntityTypeB.Object, "shared_seq");
        mockEntityTypeB.Setup(e => e.GetProperties()).Returns(new[] { mockSequencePropertyB.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityTypeA.Object, mockEntityTypeB.Object });

        var creator = CreateCreator();

        // Act & Assert - no InvalidOperationException for a "conflict" that isn't one
        await creator.EnsureCreatedAsync();

        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE SEQUENCE IF NOT EXISTS")
                                      && sql.Contains("`my-bucket`") && sql.Contains("`shared_seq`")),
                It.IsAny<QueryOptions>()),
            Times.Once);
        mockSecondaryScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE SEQUENCE IF NOT EXISTS")
                                      && sql.Contains("`secondary-bucket`") && sql.Contains("`shared_seq`")),
                It.IsAny<QueryOptions>()),
            Times.Once);
    }

    #endregion

    #region EnsureCreatedAsync Tests - Index Creation

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesDisabled_DoesNotCreateIndex()
    {
        // Arrange - AutoCreateIndexes defaults to false via the constructor setup
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - no index DDL issued when the option is off (the default)
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_CreatesPrimaryIndex()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - the primary index DDL is issued against the resolved collection's scope, and
        // the creator waits for it to report online (system:indexes check via the cluster mock,
        // which is set up in the constructor to report online immediately).
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.Contains("CREATE PRIMARY INDEX IF NOT EXISTS")
                                      && sql.Contains("`my-bucket`") && sql.Contains("`my-scope`") && sql.Contains("`TestCollection`")),
                It.IsAny<QueryOptions>()),
            Times.Once);
        _mockCluster.Verify(
            c => c.QueryAsync<int>(It.Is<string>(sql => sql.Contains("system:indexes")), It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_PropagatesCancellation_WithoutRetrying()
    {
        // Arrange - CreatePrimaryIndexAsync retries up to 10 times on transient failures, but
        // cancellation must propagate immediately instead of being treated as one of them.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        _mockScope.Setup(s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ThrowsAsync(new OperationCanceledException());

        var creator = CreateCreator();

        // Act & Assert - propagates immediately, not after retrying up to 10 times
        await Assert.ThrowsAsync<OperationCanceledException>(() => creator.EnsureCreatedAsync());

        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_WaitForOnline_PropagatesCancellation_WithoutRetrying()
    {
        // Arrange - CREATE PRIMARY INDEX succeeds normally, but the system:indexes polling query
        // inside WaitForIndexOnlineAsync is cancelled. That must propagate immediately rather than
        // being treated as a transient "keep polling" failure.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        // CREATE PRIMARY INDEX (via IScope) still succeeds; only the system:indexes check (via
        // ICluster, inside WaitForIndexOnlineAsync) is cancelled.
        _mockCluster.Setup(c => c.QueryAsync<int>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ThrowsAsync(new OperationCanceledException());

        var creator = CreateCreator();

        // Act & Assert - propagates immediately, not after polling until the 60s deadline
        await Assert.ThrowsAsync<OperationCanceledException>(() => creator.EnsureCreatedAsync());

        _mockCluster.Verify(
            c => c.QueryAsync<int>(It.IsAny<string>(), It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_TphMappedEntities_CreatesIndexOnlyOnce()
    {
        // Arrange - TPH inheritance: multiple entity types (e.g. Person, Student, Instructor in
        // modeling.md's own example) map to the SAME collection. GetEntityKeyspacesByBucket()
        // yields one entry per entity type, so without deduplication this would issue the
        // CREATE PRIMARY INDEX / online-wait once per entity type instead of once per collection.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockBaseEntityType = new Mock<IEntityType>();
        var mockBaseTableNameAnnotation = new Mock<IAnnotation>();
        mockBaseTableNameAnnotation.Setup(a => a.Value).Returns("SharedCollection");
        mockBaseEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockBaseTableNameAnnotation.Object);
        mockBaseEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockBaseEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockDerivedEntityType = new Mock<IEntityType>();
        var mockDerivedTableNameAnnotation = new Mock<IAnnotation>();
        mockDerivedTableNameAnnotation.Setup(a => a.Value).Returns("SharedCollection");
        mockDerivedEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockDerivedTableNameAnnotation.Object);
        mockDerivedEntityType.Setup(e => e.ClrType).Returns(typeof(TestDerivedEntity));
        mockDerivedEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        _mockModel.Setup(m => m.GetEntityTypes())
            .Returns(new[] { mockBaseEntityType.Object, mockDerivedEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - one CREATE PRIMARY INDEX and one online-wait for the shared collection, not two
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.Contains("CREATE PRIMARY INDEX IF NOT EXISTS") && sql.Contains("`SharedCollection`")),
                It.IsAny<QueryOptions>()),
            Times.Once);
        _mockCluster.Verify(
            c => c.QueryAsync<int>(It.Is<string>(sql => sql.Contains("system:indexes")), It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_SkipsCollectionsSkippedByAutoCreateScopes()
    {
        // Arrange - entity targets a non-default scope, but AutoCreateScopes is off, so
        // CreateCollectionsAsync never creates the collection. There is nothing to index.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("default-scope");
        _mockOptions.Setup(o => o.AutoCreateScopes).Returns(false);
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("default-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("my-bucket.other-scope.OtherCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - no index DDL for the never-created collection
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()),
            Times.Never);
    }

    #endregion

    #region EnsureCreatedAsync Tests - Secondary Index Creation (HasIndex())

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesDisabled_DoesNotCreateSecondaryIndex()
    {
        // Arrange - AutoCreateIndexes defaults to false via the constructor setup
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockIndex = CreateMockIndex(mockEntityType.Object, new[] { mockScoreProperty.Object }, "ix_score");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - no index DDL issued (neither primary nor secondary) when the option is off
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_CreatesSingleFieldSecondaryIndex()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockIndex = CreateMockIndex(mockEntityType.Object, new[] { mockScoreProperty.Object }, "ix_score");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - the secondary index DDL is issued with the index's own name, the resolved
        // collection's keyspace, and the single indexed field, and the creator waits for it to
        // report online via a name-scoped system:indexes check (no is_primary filter -- a
        // secondary index's row omits that field entirely on this server rather than setting it
        // to false, confirmed by a live spike; see WaitForSecondaryIndexOnlineAsync's own comment).
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE INDEX `ix_score` IF NOT EXISTS")
                                      && sql.Contains("`my-bucket`") && sql.Contains("`my-scope`")
                                      && sql.Contains("`TestCollection`") && sql.Contains("(`Score`)")),
                It.IsAny<QueryOptions>()),
            Times.Once);
        _mockCluster.Verify(
            c => c.QueryAsync<int>(
                It.Is<string>(sql => sql.Contains("system:indexes") && sql.Contains("name = $name")),
                It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_CreatesCompositeSecondaryIndex()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockCategoryProperty = CreateMockProperty("Category", mockEntityType.Object);
        var mockIndex = CreateMockIndex(
            mockEntityType.Object, new[] { mockScoreProperty.Object, mockCategoryProperty.Object }, "ix_score_category");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - both fields appear, in declaration order, inside a single parenthesized list
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE INDEX `ix_score_category` IF NOT EXISTS")
                                      && sql.Contains("(`Score`, `Category`)")),
                It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_CreatesFilteredSecondaryIndex()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockIndex = CreateMockIndex(
            mockEntityType.Object, new[] { mockScoreProperty.Object }, "ix_score_filtered", filter: "`Score` > 0");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - HasFilter()'s raw predicate string is spliced verbatim into a WHERE clause
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.Contains("(`Score`)") && sql.EndsWith(" WHERE `Score` > 0")),
                It.IsAny<QueryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_UniqueIndex_LogsWarningButStillCreatesIndex()
    {
        // Arrange - N1QL GSI secondary indexes cannot enforce uniqueness; IsUnique should be a
        // logged no-op warning, not a thrown error or a silently-dropped index.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockIndex = CreateMockIndex(
            mockEntityType.Object, new[] { mockScoreProperty.Object }, "ix_score_unique", isUnique: true);
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - the index is still created as a plain, non-unique index...
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE INDEX `ix_score_unique` IF NOT EXISTS")),
                It.IsAny<QueryOptions>()),
            Times.Once);

        // ...and a warning is logged explaining uniqueness cannot be enforced.
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("cannot enforce uniqueness")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_SkipsIndexOnOwnedTypeProperty()
    {
        // Arrange - an index referencing a property declared on an owned type isn't resolvable to
        // a single JSON field path on the root document in this pass; it should be skipped with a
        // warning rather than producing broken DDL.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockOwnedEntityType = new Mock<IEntityType>();
        mockOwnedEntityType.Setup(e => e.IsOwned()).Returns(true);

        var mockOwnedProperty = CreateMockProperty("Street", mockOwnedEntityType.Object);
        var mockIndex = CreateMockIndex(mockEntityType.Object, new[] { mockOwnedProperty.Object }, "ix_address_street");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - no CREATE INDEX for the owned-type-backed index
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE INDEX")),
                It.IsAny<QueryOptions>()),
            Times.Never);

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("declared on an owned type")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_ConflictingIndexDefinitionsWithSameName_Throws()
    {
        // Arrange - two distinct index definitions sharing the same database name within the same
        // keyspace is a real model bug: "CREATE INDEX ... IF NOT EXISTS" silently keeps whichever
        // definition is created first, so the actually-created index could permanently diverge from
        // one of the two definitions with no error ever surfacing. This must fail loudly instead of
        // silently overwriting the dictionary entry for the first definition.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockNameProperty = CreateMockProperty("Name", mockEntityType.Object);
        var mockIndexA = CreateMockIndex(mockEntityType.Object, new[] { mockScoreProperty.Object }, "ix_conflict");
        var mockIndexB = CreateMockIndex(mockEntityType.Object, new[] { mockNameProperty.Object }, "ix_conflict");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndexA.Object, mockIndexB.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var creator = CreateCreator();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => creator.EnsureCreatedAsync());
        Assert.Contains("ix_conflict", ex.Message);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_IdenticalIndexDefinitionsWithSameName_CreatesOnlyOnce()
    {
        // Arrange - a TPH-shared collection where two entity types happen to declare the exact
        // same index (same name, same field) is a legitimate duplicate, not a conflict -- it must
        // still dedupe to a single CREATE INDEX / online-wait, not throw.
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        var mockBaseEntityType = new Mock<IEntityType>();
        var mockBaseTableNameAnnotation = new Mock<IAnnotation>();
        mockBaseTableNameAnnotation.Setup(a => a.Value).Returns("SharedCollection");
        mockBaseEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockBaseTableNameAnnotation.Object);
        mockBaseEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockBaseEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        var mockBaseScoreProperty = CreateMockProperty("Score", mockBaseEntityType.Object);
        var mockBaseIndex = CreateMockIndex(mockBaseEntityType.Object, new[] { mockBaseScoreProperty.Object }, "ix_shared_score");
        mockBaseEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockBaseIndex.Object });

        var mockDerivedEntityType = new Mock<IEntityType>();
        var mockDerivedTableNameAnnotation = new Mock<IAnnotation>();
        mockDerivedTableNameAnnotation.Setup(a => a.Value).Returns("SharedCollection");
        mockDerivedEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockDerivedTableNameAnnotation.Object);
        mockDerivedEntityType.Setup(e => e.ClrType).Returns(typeof(TestDerivedEntity));
        mockDerivedEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        var mockDerivedScoreProperty = CreateMockProperty("Score", mockDerivedEntityType.Object);
        var mockDerivedIndex = CreateMockIndex(mockDerivedEntityType.Object, new[] { mockDerivedScoreProperty.Object }, "ix_shared_score");
        mockDerivedEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockDerivedIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes())
            .Returns(new[] { mockBaseEntityType.Object, mockDerivedEntityType.Object });

        var creator = CreateCreator();

        // Act
        await creator.EnsureCreatedAsync();

        // Assert - one CREATE INDEX and one online-wait for the shared definition, not two, and no
        // exception thrown
        _mockScope.Verify(
            s => s.QueryAsync<dynamic>(
                It.Is<string>(sql => sql.StartsWith("CREATE INDEX `ix_shared_score` IF NOT EXISTS")),
                It.IsAny<QueryOptions>()),
            Times.Once);
    }

    #endregion

    #region EnsureCreatedAsync Tests - TimeProvider-driven Deadlines and Retries

    /// <summary>
    /// Repeatedly advances <paramref name="fakeTime"/> by <paramref name="step"/>, yielding after
    /// each advance so any woken continuation gets a chance to run, until <paramref name="task"/>
    /// completes. Lets a test exercise a 60-second deadline or a multi-attempt retry-with-delay
    /// loop without an equivalent real-time wait -- <see cref="FakeTimeProvider.Advance"/> only
    /// wakes timers that are due relative to whatever the simulated clock already is at the moment
    /// it's called, so a single large jump can't skip over a chain of sequential delays; each one
    /// needs its own advance.
    /// </summary>
    private static async Task AdvanceUntilCompleteAsync(FakeTimeProvider fakeTime, Task task, TimeSpan step, int maxIterations = 500)
    {
        for (var i = 0; i < maxIterations && !task.IsCompleted; i++)
        {
            fakeTime.Advance(step);
            await Task.Yield();
        }
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_PrimaryIndexNeverComesOnline_ThrowsTimeoutExceptionWithoutRealWait()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        // Override the constructor's default (always online) so the primary index never reports online.
        _mockCluster.Setup(c => c.QueryAsync<int>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(CreateFakeQueryResult(new List<int> { 0 }));

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var fakeTime = new FakeTimeProvider();
        var creator = CreateCreator(fakeTime);

        // Act - advancing the simulated clock past the 60-second deadline, not waiting for real
        // wall-clock time to pass.
        var task = creator.EnsureCreatedAsync();
        await AdvanceUntilCompleteAsync(fakeTime, task, TimeSpan.FromSeconds(5));

        // Assert
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => task);
        Assert.Contains("Primary index", ex.Message);
        Assert.Contains("did not come online within 60 seconds", ex.Message);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_SecondaryIndexNeverComesOnline_ThrowsTimeoutExceptionWithoutRealWait()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        // Primary index: online immediately (the constructor's default -- any QueryAsync<int> call
        // returns count=1 -- covers both the online-check and ConfirmQueryableAsync's trial query).
        // Secondary index: never reports online -- overridden specifically for its name-scoped query
        // shape, which is more specific than the constructor's blanket setup and so takes precedence
        // for matching calls.
        _mockCluster.Setup(c => c.QueryAsync<int>(
                It.Is<string>(sql => sql.Contains("AND name = $name")), It.IsAny<QueryOptions>()))
            .ReturnsAsync(CreateFakeQueryResult(new List<int> { 0 }));

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());

        var mockScoreProperty = CreateMockProperty("Score", mockEntityType.Object);
        var mockIndex = CreateMockIndex(mockEntityType.Object, new[] { mockScoreProperty.Object }, "ix_score");
        mockEntityType.Setup(e => e.GetIndexes()).Returns(new[] { mockIndex.Object });

        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var fakeTime = new FakeTimeProvider();
        var creator = CreateCreator(fakeTime);

        // Act
        var task = creator.EnsureCreatedAsync();
        await AdvanceUntilCompleteAsync(fakeTime, task, TimeSpan.FromSeconds(5));

        // Assert
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => task);
        Assert.Contains("Secondary index", ex.Message);
        Assert.Contains("did not come online within 60 seconds", ex.Message);
    }

    [Fact]
    public async Task EnsureCreatedAsync_WithAutoCreateIndexesEnabled_TransientDdlFailures_RetriesUsingSimulatedTimeAndSucceeds()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("my-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("my-scope");
        _mockOptions.Setup(o => o.AutoCreateIndexes).Returns(true);

        _mockBucketManager.Setup(m => m.GetBucketAsync("my-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "my-bucket" });
        _mockCluster.Setup(c => c.BucketAsync("my-bucket")).ReturnsAsync(_mockBucket.Object);
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(new List<ScopeSpec> { new ScopeSpec("my-scope") });

        // The CREATE PRIMARY INDEX statement (issued via ExecuteDdlWithRetryAsync) fails
        // transiently on the first two attempts before succeeding on the third.
        var attempt = 0;
        _mockScope.Setup(s => s.QueryAsync<dynamic>(It.IsAny<string>(), It.IsAny<QueryOptions>()))
            .Returns(() =>
            {
                attempt++;
                return attempt <= 2
                    ? Task.FromException<IQueryResult<dynamic>>(new Exception($"transient failure {attempt}"))
                    : Task.FromResult<IQueryResult<dynamic>>(CreateFakeQueryResult(new List<dynamic>()));
            });

        var mockEntityType = new Mock<IEntityType>();
        var mockTableNameAnnotation = new Mock<IAnnotation>();
        mockTableNameAnnotation.Setup(a => a.Value).Returns("TestCollection");
        mockEntityType.Setup(e => e.FindAnnotation("Relational:TableName")).Returns(mockTableNameAnnotation.Object);
        mockEntityType.Setup(e => e.ClrType).Returns(typeof(TestEntity));
        mockEntityType.Setup(e => e.GetProperties()).Returns(Array.Empty<IProperty>());
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(new[] { mockEntityType.Object });

        var fakeTime = new FakeTimeProvider();
        var creator = CreateCreator(fakeTime);

        // Act - each of the two 1-second retry delays is advanced past using simulated time.
        var task = creator.EnsureCreatedAsync();
        await AdvanceUntilCompleteAsync(fakeTime, task, TimeSpan.FromSeconds(1));

        // Assert - succeeds once the transient failures stop, having actually reached the 3rd
        // attempt (proving the retry loop, not just a lucky first success).
        await task;
        Assert.Equal(3, attempt);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_DropsBucketWithCorrectName()
    {
        // Arrange
        _mockOptions.Setup(o => o.Bucket).Returns("bucket-to-delete");
        _mockOptions.Setup(o => o.Scope).Returns("some-scope");

        // Bucket exists
        _mockBucketManager.Setup(m => m.GetBucketAsync("bucket-to-delete", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "bucket-to-delete" });

        _mockCluster.Setup(c => c.BucketAsync("bucket-to-delete"))
            .ReturnsAsync(_mockBucket.Object);

        var creator = CreateCreator();

        // Act
        await creator.DeleteAsync();

        // Assert - should drop bucket, not scope
        _mockBucketManager.Verify(
            m => m.DropBucketAsync("bucket-to-delete", It.IsAny<DropBucketOptions>()),
            Times.Once);
        _mockBucketManager.Verify(
            m => m.DropBucketAsync("some-scope", It.IsAny<DropBucketOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenBucketNotFound_DoesNotThrow()
    {
        // Arrange
        _mockBucketManager.Setup(m => m.GetBucketAsync("test-bucket", It.IsAny<GetBucketOptions>()))
            .ThrowsAsync(new BucketNotFoundException("test-bucket"));
        _mockBucketManager.Setup(m => m.DropBucketAsync("test-bucket", It.IsAny<DropBucketOptions>()))
            .ThrowsAsync(new BucketNotFoundException("test-bucket"));

        var creator = CreateCreator();

        // Act & Assert - should not throw
        await creator.DeleteAsync();
    }

    #endregion

    #region InitializeAsync Idempotency Tests

    [Fact]
    public async Task MultipleOperations_InitializesClusterOnlyOnce()
    {
        // Arrange
        _mockBucketManager.Setup(m => m.GetBucketAsync("test-bucket", It.IsAny<GetBucketOptions>()))
            .ReturnsAsync(new BucketSettings { Name = "test-bucket" });

        var existingScopes = new List<ScopeSpec> { new ScopeSpec("test-scope") };
        _mockCollectionManager.Setup(m => m.GetAllScopesAsync(It.IsAny<GetAllScopesOptions>()))
            .ReturnsAsync(existingScopes);

        var creator = CreateCreator();

        // Act - call multiple methods that require initialization
        await creator.ExistsAsync();
        await creator.ExistsAsync();
        await creator.EnsureCreatedAsync();

        // Assert - cluster provider should only be called once
        _mockClusterProvider.Verify(
            cp => cp.GetClusterAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "InitializeAsync should be idempotent - cluster should only be retrieved once");
    }

    #endregion

    private class TestEntity
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }

    private class TestDerivedEntity : TestEntity
    {
    }
}
