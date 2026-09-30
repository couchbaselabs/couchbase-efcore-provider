using System;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Verifies that <c>CouchbaseOptionsExtensionInfo.ShouldUseSameServiceProvider</c> is consistent
/// with <c>GetServiceProviderHashCode</c>. Both must key on every setting that internal services
/// (chiefly <c>CouchbaseDatabaseCreator</c>) read off the shared <c>ICouchbaseDbContextOptionsBuilder</c>
/// singleton that lives inside the cached internal service provider: connection string, bucket,
/// scope, service key, application container, <c>AutoCreateScopes</c>, <c>AutoCreateIndexes</c>,
/// <c>ScanConsistency</c>, <c>FieldNamingPolicy</c>, and <c>SerializerOptions</c>. Two contexts
/// that differ only in one of these, but are otherwise "equivalent," must NOT share a provider —
/// otherwise one of them silently runs with the other's setting instead of its own. (This is
/// exactly the bug that motivated this test file's expansion: under concurrent test-suite load, a
/// context configured with <c>AutoCreateIndexes = true</c> silently ran as if it were <c>false</c>
/// because an earlier, otherwise-identical context's cached provider was reused.)
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
    public void DifferentAutoCreateScopes_DoesNotShareServiceProvider()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", AutoCreateScopes = false
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", AutoCreateScopes = true
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentAutoCreateIndexes_DoesNotShareServiceProvider()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", AutoCreateIndexes = false
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", AutoCreateIndexes = true
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentScanConsistency_DoesNotShareServiceProvider()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", ScanConsistency = QueryScanConsistency.NotBounded
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", ScanConsistency = QueryScanConsistency.RequestPlus
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentFieldNamingPolicy_DoesNotShareServiceProvider()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", FieldNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", FieldNamingPolicy = null
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void DifferentDateTimeFormat_DoesNotShareServiceProvider()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.FFFK"
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", DateTimeFormat = "yyyy-MM-dd"
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.False(a.ShouldUseSameServiceProvider(b));
        Assert.NotEqual(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
    }

    [Fact]
    public void DifferentSerializerOptions_DoesNotShareServiceProvider()
    {
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", SerializerOptions = new JsonSerializerOptions()
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", SerializerOptions = new JsonSerializerOptions()
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        // Different instances, even if configured identically -- SerializerOptions has no value
        // equality, so this is intentionally reference equality (see the property's own comment).
        Assert.False(a.ShouldUseSameServiceProvider(b));
    }

    [Fact]
    public void SameSerializerOptionsInstance_Shares_AndSameHashCode()
    {
        var serializerOptions = new JsonSerializerOptions();
        var aBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", SerializerOptions = serializerOptions
        };
        var bBuilder = new CouchbaseDbContextOptionsBuilder(new DbContextOptionsBuilder(), "couchbase://localhost")
        {
            Bucket = "bucketA", Scope = "scopeA", SerializerOptions = serializerOptions
        };

        var a = new CouchbaseOptionsExtension(aBuilder).Info;
        var b = new CouchbaseOptionsExtension(bBuilder).Info;

        Assert.True(a.ShouldUseSameServiceProvider(b));
        Assert.Equal(a.GetServiceProviderHashCode(), b.GetServiceProviderHashCode());
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

    // LogFragment is emitted by EF Core at Information log level, and PopulateDebugInfo feeds
    // DbContextOptions debug views -- both surface the connection string outside the provider's own
    // control, so neither may ever leak a userinfo (user:pass@) component or query-string values a
    // caller could embed a secret in. A regression here would silently put credentials back into
    // logs/debug output. All cases redact down to the same bare "couchbase://localhost" -- see
    // CouchbaseOptionsExtensionInfo.RedactConnectionString.
    [Theory]
    [InlineData("couchbase://localhost")]
    [InlineData("couchbase://admin:s3cr3t@localhost")]
    [InlineData("couchbase://localhost?username=admin&password=s3cr3t")]
    [InlineData("couchbase://admin:s3cr3t@localhost?password=s3cr3t")]
    // A password containing '@' (e.g. "p@ss") means the userinfo/host delimiter is the LAST '@',
    // not the first -- splitting on the first would leave "ss@localhost" in the redacted output,
    // still exposing part of the password.
    [InlineData("couchbase://user:p@ss@localhost")]
    public void LogFragment_RedactsCredentialsAndQueryValues(string connectionString)
    {
        var info = Extension(connectionString: connectionString).Info;

        Assert.Contains("ConnectionString: couchbase://localhost", info.LogFragment);
        AssertNoCredentialsExposed(info.LogFragment);
    }

    [Theory]
    [InlineData("couchbase://localhost")]
    [InlineData("couchbase://admin:s3cr3t@localhost")]
    [InlineData("couchbase://localhost?username=admin&password=s3cr3t")]
    [InlineData("couchbase://admin:s3cr3t@localhost?password=s3cr3t")]
    [InlineData("couchbase://user:p@ss@localhost")]
    public void PopulateDebugInfo_RedactsCredentialsAndQueryValues(string connectionString)
    {
        var info = Extension(connectionString: connectionString).Info;
        var debugInfo = new Dictionary<string, string>();

        info.PopulateDebugInfo(debugInfo);

        Assert.Equal("couchbase://localhost", debugInfo["Couchbase:ConnectionString"]);
        AssertNoCredentialsExposed(debugInfo["Couchbase:ConnectionString"]);
    }

    private static void AssertNoCredentialsExposed(string value)
    {
        Assert.Contains("localhost", value);
        Assert.DoesNotContain("admin", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s3cr3t", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", value, StringComparison.OrdinalIgnoreCase);
    }
}
