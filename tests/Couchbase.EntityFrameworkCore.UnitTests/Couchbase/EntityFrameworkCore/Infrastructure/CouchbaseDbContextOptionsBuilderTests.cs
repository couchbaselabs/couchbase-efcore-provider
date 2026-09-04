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
    public void ConnectionString_WithNoExistingQueryString_AppendsBucketWithQuestionMark()
    {
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions)
        {
            Bucket = "Universities"
        };

        // Act
        var connectionString = builder.ConnectionString;

        // Assert
        Assert.Equal("couchbase://localhost?bucket=Universities", connectionString);
    }

    [Fact]
    public void ConnectionString_WithExistingQueryString_AppendsBucketWithAmpersand()
    {
        // Regression test: Couchbase's Aspire client (from 1.0.0-beta.5) injects a connection
        // string that already carries its own query string, e.g. "?network=auto". Appending a
        // second "?bucket=..." instead of "&bucket=..." produces a malformed, unparseable
        // connection string -- GetCouchbaseClientAsync's TryGetRawParameter("bucket") then fails
        // to find the bucket and throws, which crashed the ContosoUniversity sample app on startup.
        // Arrange
        var dbContextOptionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost?network=auto");
        var builder = new CouchbaseDbContextOptionsBuilder(dbContextOptionsBuilder, clusterOptions)
        {
            Bucket = "Universities"
        };

        // Act
        var connectionString = builder.ConnectionString;

        // Assert
        Assert.Equal("couchbase://localhost?network=auto&bucket=Universities", connectionString);

        // And, concretely: the bucket parameter must be recoverable the same way
        // GetCouchbaseClientAsync recovers it, from a fresh ClusterOptions parse.
        var reparsed = new ClusterOptions().WithConnectionString(connectionString);
        Assert.True(reparsed.TryGetRawParameter("bucket", out var bucketName));
        Assert.Equal("Universities", bucketName?.ToString());
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

    [Fact]
    public void DateTimeFormat_DefaultsToIso8601MillisecondPrecision()
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");

        Assert.Equal("yyyy-MM-ddTHH:mm:ss.FFFK", builder.DateTimeFormat);
        Assert.Equal("2006-01-02T15:04:05.999Z07:00", builder.GoDateTimeFormat);
    }

    [Fact]
    public void DateTimeFormat_Setter_UpdatesGoDateTimeFormat()
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");

        builder.DateTimeFormat = "yyyy-MM-dd";

        Assert.Equal("yyyy-MM-dd", builder.DateTimeFormat);
        Assert.Equal("2006-01-02", builder.GoDateTimeFormat);
    }

    [Fact]
    public void DateTimeFormat_Setter_WithUnsupportedToken_ThrowsImmediately()
    {
        // Must fail at configuration time (when DateTimeFormat is set), per the interface's own
        // documented contract -- not deferred to whenever GoDateTimeFormat first happens to be
        // read (typically first query compilation), which would turn a configuration mistake into
        // a confusing failure far away from its cause.
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost");

        Assert.Throws<ArgumentException>(() => builder.DateTimeFormat = "yyyy-MM-dd tt");
    }

    [Fact]
    public void DateTimeFormat_Setter_WithUnsupportedToken_LeavesPreviousValueInPlace()
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            DateTimeFormat = "yyyy-MM-dd"
        };

        Assert.Throws<ArgumentException>(() => builder.DateTimeFormat = "yyyy-MM-dd tt");

        Assert.Equal("yyyy-MM-dd", builder.DateTimeFormat);
        Assert.Equal("2006-01-02", builder.GoDateTimeFormat);
    }
}
