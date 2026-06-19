using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Infrastructure;

public class CouchbaseDbContextOptionsBuilderTests
{
    [Fact]
    public void AutoCreateScopes_DefaultsToFalse()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");

        // Act
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Assert
        Assert.False(builder.AutoCreateScopes);
    }

    [Fact]
    public void AutoCreateScopes_CanBeSetToTrue()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Act
        builder.AutoCreateScopes = true;

        // Assert
        Assert.True(builder.AutoCreateScopes);
    }

    [Fact]
    public void AutoCreateScopes_CanBeSetViaInterface()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        ICouchbaseDbContextOptionsBuilder builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Act
        builder.AutoCreateScopes = true;

        // Assert
        Assert.True(builder.AutoCreateScopes);
    }

    [Fact]
    public void ScanConsistency_DefaultsToNotBounded()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");

        // Act
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Assert — preserves the SDK default; opt-in to RequestPlus only when needed.
        Assert.Equal(QueryScanConsistency.NotBounded, builder.ScanConsistency);
    }

    [Fact]
    public void ScanConsistency_CanBeSetToRequestPlus()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Act
        builder.ScanConsistency = QueryScanConsistency.RequestPlus;

        // Assert
        Assert.Equal(QueryScanConsistency.RequestPlus, builder.ScanConsistency);
    }

    [Fact]
    public void ScanConsistency_CanBeSetViaInterface()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        ICouchbaseDbContextOptionsBuilder builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Act
        builder.ScanConsistency = QueryScanConsistency.RequestPlus;

        // Assert
        Assert.Equal(QueryScanConsistency.RequestPlus, builder.ScanConsistency);
    }

    [Fact]
    public void Bucket_CanBeSet()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Act
        builder.Bucket = "testBucket";

        // Assert
        Assert.Equal("testBucket", builder.Bucket);
    }

    [Fact]
    public void Scope_CanBeSet()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Act
        builder.Scope = "testScope";

        // Assert
        Assert.Equal("testScope", builder.Scope);
    }

    [Fact]
    public void Constructor_WithConnectionString_SetsClusterOptions()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var connectionString = "couchbase://localhost";

        // Act
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, connectionString);

        // Assert
        Assert.NotNull(builder.ClusterOptions);
        Assert.Equal(connectionString, builder.ClusterOptions.ConnectionString);
    }

    [Fact]
    public void Constructor_WithClusterOptions_PreservesClusterOptions()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithCredentials("user", "password");

        // Act
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions);

        // Assert
        Assert.Same(clusterOptions, builder.ClusterOptions);
    }
}
