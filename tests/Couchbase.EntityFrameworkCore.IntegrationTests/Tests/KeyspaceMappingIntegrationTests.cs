using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class KeyspaceMappingIntegrationTests
{
    private readonly BloggingFixture _fixture;

    public KeyspaceMappingIntegrationTests(BloggingFixture fixture)
    {
        _fixture = fixture;
    }

    #region ToCouchbaseCollection Tests

    [Fact]
    public void ToCouchbaseCollection_WithCollectionName_SetsCorrectKeyspace()
    {
        // Arrange & Act
        using var context = CreateContext<ToCouchbaseCollectionContext>();
        var entityType = context.Model.FindEntityType(typeof(TestEntity))!;

        // Assert - Should be Bucket.Scope.Collection format
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);
        
        // Parse and verify it's in the correct format
        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("my-collection", keyspace!.Value.Collection);
    }

    [Fact]
    public void ToCouchbaseCollection_WithScopeAndCollection_SetsCorrectKeyspace()
    {
        // Arrange & Act
        using var context = CreateContext<ToCouchbaseCollectionWithScopeContext>();
        var entityType = context.Model.FindEntityType(typeof(TestEntity))!;

        // Assert
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);
        
        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("custom-scope", keyspace!.Value.Scope);
        Assert.Equal("custom-collection", keyspace.Value.Collection);
    }

    #endregion

    #region ConfigureToCouchbase Tests

    [Fact]
    public void ConfigureToCouchbase_WithPlainEntity_AddsFullKeyspace()
    {
        // Arrange & Act
        using var context = CreateContext<ConfigureToCouchbaseContext>();
        var entityType = context.Model.FindEntityType(typeof(PlainTestEntity))!;

        // Assert - Should have full keyspace
        // EF Core uses the DbSet property name as the default table name
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);
        
        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("PlainEntities", keyspace!.Value.Collection);
    }

    [Fact]
    public void ConfigureToCouchbase_WithLowerCaseNaming_ConvertsToLowerCase()
    {
        // Arrange & Act
        using var context = CreateContext<ConfigureToCouchbaseLowerCaseContext>();
        var entityType = context.Model.FindEntityType(typeof(PlainTestEntity))!;

        // Assert - EF Core uses DbSet name, then toLowerCaseNaming converts it
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);
        
        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("plainentities", keyspace!.Value.Collection);
    }

    [Fact]
    public void ConfigureToCouchbase_WithExistingFullKeyspace_DoesNotModify()
    {
        // Arrange & Act
        using var context = CreateContext<ConfigureToCouchbaseWithExistingKeyspaceContext>();
        var entityType = context.Model.FindEntityType(typeof(TestEntity))!;

        // Assert - Should preserve the manually set keyspace
        var tableName = entityType.GetTableName();
        Assert.Equal("other-bucket.other-scope.other-collection", tableName);
    }

    #endregion

    #region CouchbaseKeyspaceAttribute Tests

    [Fact]
    public void CouchbaseKeyspaceAttribute_WithCollectionOnly_UsesDbContextScope()
    {
        // Arrange & Act
        using var context = CreateContext<AttributeEntityContext>();
        var entityType = context.Model.FindEntityType(typeof(EntityWithAttribute))!;

        // Assert - Should use DbContext scope since attribute doesn't specify scope
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);
        
        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("users-from-attribute", keyspace!.Value.Collection);
        Assert.Equal(_fixture.ScopeName, keyspace.Value.Scope); // Uses DbContext scope
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_WithScopeAndCollection_UsesScopeOverride()
    {
        // Arrange & Act
        using var context = CreateContext<AttributeWithScopeEntityContext>();
        var entityType = context.Model.FindEntityType(typeof(EntityWithScopeAttribute))!;

        // Assert - Should use the scope from the attribute, not DbContext
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);

        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("products-collection", keyspace!.Value.Collection);
        Assert.Equal("custom-scope-attr", keyspace.Value.Scope); // Uses attribute scope override
    }

    [Fact]
    public void CouchbaseKeyspaceAttribute_ScopeOverride_DifferentFromDbContextScope()
    {
        // Arrange & Act
        using var context = CreateContext<AttributeWithScopeEntityContext>();
        var entityType = context.Model.FindEntityType(typeof(EntityWithScopeAttribute))!;

        // Assert - Scope from attribute should be different from DbContext scope
        var tableName = entityType.GetTableName();
        Assert.True(CouchbaseKeyspace.TryParse(tableName!, out var keyspace));

        Assert.NotEqual(_fixture.ScopeName, keyspace!.Value.Scope);
        Assert.Equal("custom-scope-attr", keyspace.Value.Scope);
    }

    #endregion

    #region Conjunction Tests (Attribute + Fluent API)

    [Fact]
    public void FluentApi_OverridesAttribute_WhenBothUsed()
    {
        // Arrange & Act
        using var context = CreateContext<FluentOverridesAttributeContext>();
        var entityType = context.Model.FindEntityType(typeof(EntityWithAttribute))!;

        // Assert - Fluent API should override attribute
        var tableName = entityType.GetTableName();
        Assert.NotNull(tableName);
        
        Assert.True(CouchbaseKeyspace.TryParse(tableName, out var keyspace));
        Assert.Equal("overridden-by-fluent", keyspace!.Value.Collection);
    }

    [Fact]
    public void ToCouchbaseCollection_ThenConfigureToCouchbase_PreservesExplicitKeyspace()
    {
        // Arrange & Act
        using var context = CreateContext<ToCouchbaseCollectionThenConfigureContext>();
        var plainEntity = context.Model.FindEntityType(typeof(PlainTestEntity))!;
        var explicitEntity = context.Model.FindEntityType(typeof(TestEntity))!;

        // Assert - PlainTestEntity uses ConfigureToCouchbase (DbSet name lowercased), TestEntity uses explicit
        var plainTableName = plainEntity.GetTableName();
        var explicitTableName = explicitEntity.GetTableName();

        Assert.True(CouchbaseKeyspace.TryParse(plainTableName!, out var plainKeyspace));
        Assert.True(CouchbaseKeyspace.TryParse(explicitTableName!, out var explicitKeyspace));
        
        Assert.Equal("plainentities", plainKeyspace!.Value.Collection); // DbSet name lowercased
        Assert.Equal("explicit-collection", explicitKeyspace!.Value.Collection);
    }

    [Fact]
    public void MultipleEntities_DifferentMappingMethods_EachMappedCorrectly()
    {
        // Arrange & Act
        using var context = CreateContext<MultipleEntitiesContext>();

        var plainEntity = context.Model.FindEntityType(typeof(PlainTestEntity))!;
        var attributeEntity = context.Model.FindEntityType(typeof(EntityWithAttribute))!;
        var explicitEntity = context.Model.FindEntityType(typeof(TestEntity))!;

        // Assert - EF Core uses DbSet property names as default table names
        var plainTableName = plainEntity.GetTableName();
        var attributeTableName = attributeEntity.GetTableName();
        var explicitTableName = explicitEntity.GetTableName();

        Assert.True(CouchbaseKeyspace.TryParse(plainTableName!, out var plainKeyspace));
        Assert.True(CouchbaseKeyspace.TryParse(attributeTableName!, out var attributeKeyspace));
        Assert.True(CouchbaseKeyspace.TryParse(explicitTableName!, out var explicitKeyspace));
        
        Assert.Equal("plainentities", plainKeyspace!.Value.Collection); // DbSet name lowercased
        Assert.Equal("users-from-attribute", attributeKeyspace!.Value.Collection); // Attribute overrides DbSet name
        Assert.Equal("explicit-entity", explicitKeyspace!.Value.Collection);
    }

    #endregion

    #region Test Entities

    public class TestEntity
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
    }

    public class PlainTestEntity
    {
        public string Id { get; set; } = null!;
        public string Value { get; set; } = null!;
    }

    [CouchbaseKeyspace("users-from-attribute")]
    public class EntityWithAttribute
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
    }

    [CouchbaseKeyspace("custom-scope-attr", "products-collection")]
    public class EntityWithScopeAttribute
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
    }

    #endregion

    #region Test DbContexts

    private class ToCouchbaseCollectionContext : DbContext
    {
        public ToCouchbaseCollectionContext(DbContextOptions options) : base(options) { }
        public DbSet<TestEntity> TestEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().ToCouchbaseCollection(this, "my-collection");
        }
    }

    private class ToCouchbaseCollectionWithScopeContext : DbContext
    {
        public ToCouchbaseCollectionWithScopeContext(DbContextOptions options) : base(options) { }
        public DbSet<TestEntity> TestEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().ToCouchbaseCollection(this, "custom-scope", "custom-collection");
        }
    }

    private class ConfigureToCouchbaseContext : DbContext
    {
        public ConfigureToCouchbaseContext(DbContextOptions options) : base(options) { }
        public DbSet<PlainTestEntity> PlainEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlainTestEntity>();
            modelBuilder.ConfigureToCouchbase(this);
        }
    }

    private class ConfigureToCouchbaseLowerCaseContext : DbContext
    {
        public ConfigureToCouchbaseLowerCaseContext(DbContextOptions options) : base(options) { }
        public DbSet<PlainTestEntity> PlainEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlainTestEntity>();
            modelBuilder.ConfigureToCouchbase(this, toLowerCaseNaming: true);
        }
    }

    private class ConfigureToCouchbaseWithExistingKeyspaceContext : DbContext
    {
        public ConfigureToCouchbaseWithExistingKeyspaceContext(DbContextOptions options) : base(options) { }
        public DbSet<TestEntity> TestEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().ToTable("other-bucket.other-scope.other-collection");
            modelBuilder.ConfigureToCouchbase(this);
        }
    }

    private class AttributeEntityContext : DbContext
    {
        public AttributeEntityContext(DbContextOptions options) : base(options) { }
        public DbSet<EntityWithAttribute> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EntityWithAttribute>();
            modelBuilder.ConfigureToCouchbase(this);
        }
    }

    private class AttributeWithScopeEntityContext : DbContext
    {
        public AttributeWithScopeEntityContext(DbContextOptions options) : base(options) { }
        public DbSet<EntityWithScopeAttribute> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EntityWithScopeAttribute>();
            modelBuilder.ConfigureToCouchbase(this);
        }
    }

    private class FluentOverridesAttributeContext : DbContext
    {
        public FluentOverridesAttributeContext(DbContextOptions options) : base(options) { }
        public DbSet<EntityWithAttribute> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Fluent API should override the attribute
            modelBuilder.Entity<EntityWithAttribute>().ToCouchbaseCollection(this, "overridden-by-fluent");
        }
    }

    private class ToCouchbaseCollectionThenConfigureContext : DbContext
    {
        public ToCouchbaseCollectionThenConfigureContext(DbContextOptions options) : base(options) { }
        public DbSet<TestEntity> TestEntities { get; set; } = null!;
        public DbSet<PlainTestEntity> PlainEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().ToCouchbaseCollection(this, "explicit-collection");
            modelBuilder.Entity<PlainTestEntity>();
            modelBuilder.ConfigureToCouchbase(this, toLowerCaseNaming: true);
        }
    }

    private class MultipleEntitiesContext : DbContext
    {
        public MultipleEntitiesContext(DbContextOptions options) : base(options) { }
        public DbSet<TestEntity> TestEntities { get; set; } = null!;
        public DbSet<PlainTestEntity> PlainEntities { get; set; } = null!;
        public DbSet<EntityWithAttribute> AttributeEntities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlainTestEntity>(); // Uses ConfigureToCouchbase
            modelBuilder.Entity<EntityWithAttribute>(); // Uses attribute
            modelBuilder.Entity<TestEntity>().ToCouchbaseCollection(this, "explicit-entity");
            modelBuilder.ConfigureToCouchbase(this, toLowerCaseNaming: true);
        }
    }

    #endregion

    #region DbContext Configuration Validation Tests

    [Fact]
    public void ConfigureToCouchbase_WithNullBucket_ThrowsArgumentException()
    {
        // Arrange
        var context = CreateContextWithNullBucket<PlainEntityContext>();

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => context.Model);
        Assert.Contains("Bucket", exception.Message);
    }

    [Fact]
    public void ConfigureToCouchbase_WithEmptyBucket_ThrowsArgumentException()
    {
        // Arrange
        var context = CreateContextWithEmptyBucket<PlainEntityContext>();

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => context.Model);
        Assert.Contains("Bucket", exception.Message);
    }

    [Fact]
    public void ConfigureToCouchbase_WithNullScope_ThrowsArgumentException()
    {
        // Arrange
        var context = CreateContextWithNullScope<PlainEntityContext>();

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => context.Model);
        Assert.Contains("Scope", exception.Message);
    }

    [Fact]
    public void ConfigureToCouchbase_WithEmptyScope_ThrowsArgumentException()
    {
        // Arrange
        var context = CreateContextWithEmptyScope<PlainEntityContext>();

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => context.Model);
        Assert.Contains("Scope", exception.Message);
    }

    private class PlainEntityContext : DbContext
    {
        public PlainEntityContext(DbContextOptions options) : base(options) { }
        public DbSet<PlainTestEntity> Entities { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlainTestEntity>();
            modelBuilder.ConfigureToCouchbase(this);
        }
    }

    #endregion

    #region Test Infrastructure

    private TContext CreateContext<TContext>() where TContext : DbContext
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
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

        return (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
    }

    private TContext CreateContextWithNullBucket<TContext>() where TContext : DbContext
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password)
                .WithLogging(loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = null!;
                couchbaseDbContextOptions.Scope = _fixture.ScopeName;
            });

        return (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
    }

    private TContext CreateContextWithEmptyBucket<TContext>() where TContext : DbContext
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password)
                .WithLogging(loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "";
                couchbaseDbContextOptions.Scope = _fixture.ScopeName;
            });

        return (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
    }

    private TContext CreateContextWithNullScope<TContext>() where TContext : DbContext
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password)
                .WithLogging(loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = _fixture.BucketName;
                couchbaseDbContextOptions.Scope = null!;
            });

        return (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
    }

    private TContext CreateContextWithEmptyScope<TContext>() where TContext : DbContext
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
        });

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(_fixture.Host)
                .WithPasswordAuthentication(_fixture.Username, _fixture.Password)
                .WithLogging(loggerFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = _fixture.BucketName;
                couchbaseDbContextOptions.Scope = "";
            });

        return (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
    }

    #endregion
}
