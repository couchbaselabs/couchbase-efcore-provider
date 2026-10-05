using System;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    // A literal '?' before the final '@' (e.g. an unescaped '?' inside a password) means the value
    // can't be parsed as a clean scheme://[userinfo@]host[?query] shape -- truncating at the first
    // '?' would cut off the '@' delimiter along with it ("user:p?ss@localhost" would truncate to
    // "user:p", still leaking a credential fragment). Rather than guess where userinfo ends and the
    // query begins, the whole value is redacted instead of just the host being retained.
    //
    // This can't be exercised through the Extension() helper's connection-string constructor:
    // ClusterOptions.WithConnectionString percent-encodes ':' and '?' found in userinfo before
    // RedactConnectionString ever sees them, which would silently neutralize the very input this
    // test needs to send. Going through WithConnection(DbConnection) instead -- a real, public EF
    // Core relational-provider configuration path -- uses the raw ADO.NET ConnectionString
    // untouched by the Couchbase SDK, so the literal '?' actually reaches the method under test.
    [Fact]
    public void LogFragment_RedactsWholeValue_WhenQuestionMarkPrecedesFinalAt()
    {
        var info = ExtensionWithConnection("couchbase://user:p?ss@localhost").Info;

        Assert.Contains($"ConnectionString: {CouchbaseOptionsExtension.CouchbaseOptionsExtensionInfo.RedactedConnectionStringPlaceholder}", info.LogFragment);
        Assert.DoesNotContain("user", info.LogFragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ss@localhost", info.LogFragment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PopulateDebugInfo_RedactsWholeValue_WhenQuestionMarkPrecedesFinalAt()
    {
        var info = ExtensionWithConnection("couchbase://user:p?ss@localhost").Info;
        var debugInfo = new Dictionary<string, string>();

        info.PopulateDebugInfo(debugInfo);

        Assert.Equal(CouchbaseOptionsExtension.CouchbaseOptionsExtensionInfo.RedactedConnectionStringPlaceholder, debugInfo["Couchbase:ConnectionString"]);
    }

    // A raw connection string reaching this method via WithConnection(DbConnection) is not
    // guaranteed to start with a real "couchbase://"/"couchbases://" scheme. If a credential
    // happens to contain "://" (e.g. "user:p://ss@localhost"), blindly treating the FIRST "://" in
    // the value as the scheme separator mistakes that embedded "://" for one, and the actual
    // credential prefix ("user:p") gets treated as a harmless scheme and retained verbatim
    // ("user:p://localhost") instead of being redacted.
    [Fact]
    public void LogFragment_RedactsWholeValue_WhenSchemeIsNotRecognizedAndSlashSlashIsAmbiguous()
    {
        var info = ExtensionWithConnection("user:p://ss@localhost").Info;

        Assert.Contains($"ConnectionString: {CouchbaseOptionsExtension.CouchbaseOptionsExtensionInfo.RedactedConnectionStringPlaceholder}", info.LogFragment);
        Assert.DoesNotContain("user", info.LogFragment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PopulateDebugInfo_RedactsWholeValue_WhenSchemeIsNotRecognizedAndSlashSlashIsAmbiguous()
    {
        var info = ExtensionWithConnection("user:p://ss@localhost").Info;
        var debugInfo = new Dictionary<string, string>();

        info.PopulateDebugInfo(debugInfo);

        Assert.Equal(CouchbaseOptionsExtension.CouchbaseOptionsExtensionInfo.RedactedConnectionStringPlaceholder, debugInfo["Couchbase:ConnectionString"]);
    }

    // A raw connection string reaching this method via WithConnection(DbConnection) need not look
    // like a URI at all -- an ADO.NET-style string such as "Server=localhost;User ID=admin;
    // Password=s3cr3t" contains none of '?', '@', or "://", so without an up-front allow-list for
    // recognized Couchbase schemes, none of the query/userinfo stripping logic would ever trigger
    // and the password would be returned completely unredacted.
    [Fact]
    public void LogFragment_RedactsWholeValue_WhenConnectionStringIsNotUriShaped()
    {
        var info = ExtensionWithConnection("Server=localhost;User ID=admin;Password=s3cr3t").Info;

        Assert.Contains($"ConnectionString: {CouchbaseOptionsExtension.CouchbaseOptionsExtensionInfo.RedactedConnectionStringPlaceholder}", info.LogFragment);
        Assert.DoesNotContain("s3cr3t", info.LogFragment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PopulateDebugInfo_RedactsWholeValue_WhenConnectionStringIsNotUriShaped()
    {
        var info = ExtensionWithConnection("Server=localhost;User ID=admin;Password=s3cr3t").Info;
        var debugInfo = new Dictionary<string, string>();

        info.PopulateDebugInfo(debugInfo);

        Assert.Equal(CouchbaseOptionsExtension.CouchbaseOptionsExtensionInfo.RedactedConnectionStringPlaceholder, debugInfo["Couchbase:ConnectionString"]);
    }

    private static CouchbaseOptionsExtension ExtensionWithConnection(string rawConnectionString)
    {
        var withConnection = ((RelationalOptionsExtension)Extension())
            .WithConnection(new FakeDbConnection { ConnectionString = rawConnectionString });
        return (CouchbaseOptionsExtension)withConnection;
    }

    // Minimal stub: only ConnectionString is read by RedactConnectionString. Every other member
    // is unused by this test and left unimplemented.
    private sealed class FakeDbConnection : DbConnection
    {
        public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
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
