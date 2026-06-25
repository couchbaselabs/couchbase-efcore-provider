using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Verifies that <c>CouchbaseOptionsExtensionInfo.ShouldUseSameServiceProvider</c> is consistent
/// with <c>GetServiceProviderHashCode</c>. Both must key on (connection string, bucket, scope,
/// service key) so that contexts pointing at different buckets/clusters get their own internal
/// service provider — each registers its own Couchbase cluster/bucket provider. (Multi-bucket DI, step 1.)
/// </summary>
public class CouchbaseOptionsExtensionInfoTests
{
    private static CouchbaseOptionsExtension Extension(
        string connectionString = "couchbase://localhost",
        string bucket = "bucketA",
        string scope = "scopeA")
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), connectionString)
        {
            Bucket = bucket,
            Scope = scope
        };
        return new CouchbaseOptionsExtension(builder);
    }

    [Fact]
    public void SameConfig_SharesServiceProvider_AndSameHashCode()
    {
        var a = Extension().Info;
        var b = Extension().Info;

        Assert.True(a.ShouldUseSameServiceProvider(b));
        Assert.Equal(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }

    [Fact]
    public void DifferentBucket_DoesNotShare_AndDifferentHashCode()
    {
        var a = Extension(bucket: "bucketA").Info;
        var b = Extension(bucket: "bucketB").Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
        Assert.NotEqual(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }

    [Fact]
    public void DifferentScope_DoesNotShare_AndDifferentHashCode()
    {
        var a = Extension(scope: "scopeA").Info;
        var b = Extension(scope: "scopeB").Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
        Assert.NotEqual(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }

    [Fact]
    public void DifferentConnectionString_DoesNotShare_AndDifferentHashCode()
    {
        var a = Extension(connectionString: "couchbase://host-a").Info;
        var b = Extension(connectionString: "couchbase://host-b").Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
        Assert.NotEqual(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }

    [Fact]
    public void DifferentServiceKey_DoesNotShare_AndDifferentHashCode()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA",
            Scope = "scopeA",
            ServiceKey = "clusterA"
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA",
            Scope = "scopeA",
            ServiceKey = "clusterB"
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
        Assert.NotEqual(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }
}
