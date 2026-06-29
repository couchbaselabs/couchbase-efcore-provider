using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Couchbase;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Core.IO.Serializers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseOptionsExtension: RelationalOptionsExtension
{
    private readonly CouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private CouchbaseOptionsExtensionInfo? _info;

    public CouchbaseOptionsExtension(CouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }
    protected internal CouchbaseOptionsExtension(CouchbaseOptionsExtension copyFrom)
        : base(copyFrom)
    {
        _couchbaseDbContextOptionsBuilder = copyFrom.CouchbaseDbContextOptionsBuilder;
    }

    public CouchbaseDbContextOptionsBuilder? CouchbaseDbContextOptionsBuilder => _couchbaseDbContextOptionsBuilder;

    public override string? ConnectionString => _couchbaseDbContextOptionsBuilder.ConnectionString;

    public override DbContextOptionsExtensionInfo Info => _info ??= new CouchbaseOptionsExtensionInfo(this);

    public CouchbaseDbContextOptionsBuilder DbContextOptionsBuilder => _couchbaseDbContextOptionsBuilder;

    public override void ApplyServices(IServiceCollection services)
    {
        services.AddCouchbase(options =>
        {
            options.WithLogging(_couchbaseDbContextOptionsBuilder.ClusterOptions.Logging);
            options.WithConnectionString(_couchbaseDbContextOptionsBuilder.ClusterOptions.ConnectionString);

            // Use existing serializer if configured, otherwise default to System.Text.Json
            var existingSerializer = _couchbaseDbContextOptionsBuilder.ClusterOptions.Serializer;
            if (existingSerializer is null || existingSerializer is DefaultSerializer)
            {
                options.WithSerializer(SystemTextJsonSerializer.Create());
            }
            else
            {
                options.WithSerializer(existingSerializer);
            }

            if (_couchbaseDbContextOptionsBuilder.ClusterOptions.Authenticator != null)
            {
                options.WithAuthenticator(_couchbaseDbContextOptionsBuilder.ClusterOptions.Authenticator);
            }
            else
            {
                // No Authenticator was set, so the source ClusterOptions carries legacy
                // username/password. Forward them via the supported WithPasswordAuthentication
                // (equivalent to the obsolete WithCredentials). Reading UserName/Password is itself
                // obsolete in the SDK and has no non-obsolete equivalent for legacy configs, so
                // suppress locally — once the SDK removes those members this stops compiling and
                // forces Authenticator-based configuration.
#pragma warning disable CS0618 // Type or member is obsolete
                options.WithPasswordAuthentication(
                    _couchbaseDbContextOptionsBuilder.ClusterOptions.UserName,
                    _couchbaseDbContextOptionsBuilder.ClusterOptions.Password);
#pragma warning restore CS0618
            }
        });

        // If the application registered its own Couchbase cluster in DI, bind this context to that
        // shared IClusterProvider (one Cluster per server, opens many buckets — per Couchbase
        // guidance) instead of the per-context cluster registered above. The application owns the
        // cluster's lifetime, so it is wrapped to prevent EF's internal provider from disposing it.
        var sharedClusterProvider = TryResolveSharedClusterProvider();
        if (sharedClusterProvider is not null)
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                var serviceType = services[i].ServiceType;
                if (serviceType == typeof(IClusterProvider) || serviceType == typeof(IBucketProvider))
                {
                    services.RemoveAt(i);
                }
            }

            services.AddSingleton<IClusterProvider>(new NonOwningClusterProvider(sharedClusterProvider));
            services.AddSingleton<IBucketProvider>(sp =>
                new SharedClusterBucketProvider(sp.GetRequiredService<IClusterProvider>()));
        }

        services.AddEntityFrameworkCouchbase(this);
    }

    /// <summary>
    /// Resolves an application-registered <see cref="IClusterProvider"/> to share, or null when the
    /// context owns its own cluster (no application provider captured, or none registered in DI).
    /// </summary>
    private IClusterProvider? TryResolveSharedClusterProvider()
    {
        var applicationServiceProvider = _couchbaseDbContextOptionsBuilder.ApplicationServiceProvider;
        if (applicationServiceProvider is null)
        {
            return null;
        }

        var serviceKey = _couchbaseDbContextOptionsBuilder.ServiceKey;
        if (serviceKey is null)
        {
            // Use the unkeyed application cluster if one was registered (services.AddCouchbase(...)).
            return applicationServiceProvider.GetService<IClusterProvider>();
        }

        // A ServiceKey was specified: the keyed cluster must exist — fail loudly otherwise so a
        // misconfiguration is not silently masked by spinning up a separate per-context cluster.
        var keyedClusterProvider = applicationServiceProvider.GetKeyedService<IClusterProvider>(serviceKey);
        if (keyedClusterProvider is null)
        {
            throw new InvalidOperationException(
                $"No keyed Couchbase cluster is registered for ServiceKey '{serviceKey}'. " +
                $"Call services.AddKeyedCouchbase(\"{serviceKey}\", ...) before " +
                $"AddCouchbase<TContext>(..., o => o.ServiceKey = \"{serviceKey}\").");
        }

        return keyedClusterProvider;
    }

    public override void Validate(IDbContextOptions options)
    {
        // You can add any validation logic here, if necessary.
    }

    protected override RelationalOptionsExtension Clone() => new CouchbaseOptionsExtension(this);


    public class CouchbaseOptionsExtensionInfo : DbContextOptionsExtensionInfo
    {
        public CouchbaseOptionsExtensionInfo(CouchbaseOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => true;

        public override string LogFragment => $"Using Custom Couchbase Provider - ConnectionString: {ConnectionString}";

        // A stable identity for the application's DI container, or null when configured outside DI
        // (plain UseCouchbase). ApplyServices can bind an application-registered shared cluster into
        // the (process-wide cached) internal service provider, so the cache key must distinguish
        // application containers — otherwise a different root IServiceProvider with the same
        // connection/bucket/scope/key could reuse an internal provider wired to the wrong
        // container's cluster (or to a cluster from a disposed container). The identity (the root's
        // IServiceScopeFactory) is captured eagerly when ApplicationServiceProvider is set, so this
        // equality/hash path never resolves services from a possibly-disposed provider.
        private object? ApplicationContainerIdentity
            => Extension._couchbaseDbContextOptionsBuilder.ApplicationContainerIdentity;

        public override int GetServiceProviderHashCode() => HashCode.Combine(
            ConnectionString,
            Extension._couchbaseDbContextOptionsBuilder.Bucket,
            Extension._couchbaseDbContextOptionsBuilder.Scope,
            Extension._couchbaseDbContextOptionsBuilder.ServiceKey,
            RuntimeHelpers.GetHashCode(ApplicationContainerIdentity));

        // Must be consistent with GetServiceProviderHashCode: two contexts can share an internal
        // service provider only when their connection string, bucket, scope, service key, and
        // application container all match. Each distinct combination registers its own Couchbase
        // cluster/bucket provider (see ApplyServices), so collapsing them onto one provider would
        // resolve the wrong bucket or cluster.
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is CouchbaseOptionsExtensionInfo otherInfo
                && ConnectionString == otherInfo.ConnectionString
                && Extension._couchbaseDbContextOptionsBuilder.Bucket == otherInfo.Extension._couchbaseDbContextOptionsBuilder.Bucket
                && Extension._couchbaseDbContextOptionsBuilder.Scope == otherInfo.Extension._couchbaseDbContextOptionsBuilder.Scope
                && Equals(Extension._couchbaseDbContextOptionsBuilder.ServiceKey, otherInfo.Extension._couchbaseDbContextOptionsBuilder.ServiceKey)
                && ReferenceEquals(ApplicationContainerIdentity, otherInfo.ApplicationContainerIdentity);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Couchbase:ConnectionString"] = ConnectionString;
        }

        public override CouchbaseOptionsExtension Extension => (CouchbaseOptionsExtension)base.Extension;

        private string? ConnectionString => Extension.Connection == null ?
            Extension.ConnectionString :
            Extension.Connection.ConnectionString;
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
