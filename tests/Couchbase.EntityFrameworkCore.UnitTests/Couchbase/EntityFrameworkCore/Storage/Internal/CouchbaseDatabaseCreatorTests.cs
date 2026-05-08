using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        _mockCollectionManager = new Mock<ICouchbaseCollectionManager>();
        _mockBucketManager = new Mock<IBucketManager>();
        _mockModel = new Mock<IModel>();

        // Default setup
        _mockOptions.Setup(o => o.Bucket).Returns("test-bucket");
        _mockOptions.Setup(o => o.Scope).Returns("test-scope");
        _mockOptions.Setup(o => o.AutoCreateScopes).Returns(false);

        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IClusterProvider)))
            .Returns(_mockClusterProvider.Object);
        _mockClusterProvider.Setup(cp => cp.GetClusterAsync())
            .ReturnsAsync(_mockCluster.Object);
        _mockCluster.Setup(c => c.BucketAsync("test-bucket"))
            .ReturnsAsync(_mockBucket.Object);
        _mockCluster.Setup(c => c.Buckets).Returns(_mockBucketManager.Object);
        _mockBucket.Setup(b => b.Collections).Returns(_mockCollectionManager.Object);

        _mockDesignTimeModel.Setup(m => m.Model).Returns(_mockModel.Object);
        _mockModel.Setup(m => m.GetEntityTypes()).Returns(Array.Empty<IEntityType>());

        _mockSqlGenerationHelper.Setup(h => h.DelimitIdentifier(It.IsAny<string>()))
            .Returns<string>(s => $"`{s}`");
    }

    private CouchbaseDatabaseCreator CreateCreator()
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
            _mockSqlGenerationHelper.Object);
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
            m => m.CreateCollectionAsync("my-scope", "TestCollection", It.IsAny<CreateCollectionSettings>()),
            Times.Once);
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
            m => m.CreateCollectionAsync("other-scope", "OtherCollection", It.IsAny<CreateCollectionSettings>()),
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
            m => m.CreateCollectionAsync("other-scope", It.IsAny<string>(), It.IsAny<CreateCollectionSettings>()),
            Times.Never);
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
            cp => cp.GetClusterAsync(),
            Times.Once,
            "InitializeAsync should be idempotent - cluster should only be retrieved once");
    }

    #endregion

    private class TestEntity
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }
}
