using System;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Verifies that <c>CouchbaseOptionsExtensionInfo.ShouldUseSameServiceProvider</c> is consistent
/// with <c>GetServiceProviderHashCode</c>. Both must key on (connection string, bucket, scope,
/// service key, and application container) so that contexts pointing at different buckets/clusters
/// — or registered in different DI containers — get their own internal service provider, each
/// registering its own Couchbase cluster/bucket provider. (Multi-bucket DI.)
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

    // The "Different…" tests assert only ShouldUseSameServiceProvider == false: that is the
    // contract EF Core relies on to keep internal providers separate. Hash codes are not required
    // to differ (collisions are permitted and disambiguated by ShouldUseSameServiceProvider), so
    // asserting hash inequality would test a property that is not guaranteed even when correct.
    [Fact]
    public void DifferentBucket_DoesNotShareServiceProvider()
    {
        var a = Extension(bucket: "bucketA").Info;
        var b = Extension(bucket: "bucketB").Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentScope_DoesNotShareServiceProvider()
    {
        var a = Extension(scope: "scopeA").Info;
        var b = Extension(scope: "scopeB").Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentConnectionString_DoesNotShareServiceProvider()
    {
        var a = Extension(connectionString: "couchbase://host-a").Info;
        var b = Extension(connectionString: "couchbase://host-b").Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentServiceKey_DoesNotShareServiceProvider()
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
    }

    [Fact]
    public void DifferentApplicationContainer_DoesNotShareServiceProvider()
    {
        // ApplyServices can bind a specific application container's shared cluster into the
        // (process-wide cached) internal provider, so two identical configurations in DIFFERENT
        // containers must not share an internal provider.
        using var containerA = new ServiceCollection().BuildServiceProvider();
        using var containerB = new ServiceCollection().BuildServiceProvider();

        var a = ExtensionWithApplicationProvider(containerA).Info;
        var b = ExtensionWithApplicationProvider(containerB).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void SameApplicationContainer_Shares_AndSameHashCode()
    {
        using var container = new ServiceCollection().BuildServiceProvider();

        var a = ExtensionWithApplicationProvider(container).Info;
        var b = ExtensionWithApplicationProvider(container).Info;

        Assert.True(a.ShouldUseSameServiceProvider(b));
        Assert.Equal(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }

    private static CouchbaseOptionsExtension ExtensionWithApplicationProvider(IServiceProvider applicationServiceProvider)
    {
        var builder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA",
            Scope = "scopeA",
            ApplicationServiceProvider = applicationServiceProvider
        };
        return new CouchbaseOptionsExtension(builder);
    }
}
