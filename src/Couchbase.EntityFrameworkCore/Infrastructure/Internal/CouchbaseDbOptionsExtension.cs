using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Couchbase;
using Couchbase.EntityFrameworkCore.Extensions;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseDbOptionsExtension : IDbContextOptionsExtension
{
    private ClusterOptions? _clusterOptions;
    private string? _connectionString;
    private string? _bucketName;
    private ICluster? _cluster;
    private DbContextOptionsExtensionInfo? _info;
    
    public CouchbaseDbOptionsExtension()
    {
    }

    public CouchbaseDbOptionsExtension(CouchbaseDbOptionsExtension copyFrom)
    {
        _cluster = copyFrom._cluster;
    }

    public CouchbaseDbOptionsExtension(ICluster cluster)
    {
        _cluster = cluster;
    }

    public CouchbaseDbOptionsExtension(ClusterOptions clusterOptions)
    {
        _clusterOptions = clusterOptions;
    }

    public CouchbaseDbOptionsExtension(string connectionString, string bucketName, ClusterOptions clusterOptions = null)
    {
        _connectionString = connectionString;
        _bucketName = bucketName;
        _clusterOptions = clusterOptions;
    }

    public CouchbaseDbOptionsExtension WithCluster(ICluster cluster)
    {
        var clone = Clone();
        clone._cluster = cluster;
        return clone;
    }

    public CouchbaseDbOptionsExtension WithConnectionString(string connectionString)
    {
        var clone = Clone();
        clone._connectionString = connectionString;
        return clone;
    }

    private CouchbaseDbOptionsExtension Clone() => new(this);
    
    public void ApplyServices(IServiceCollection services) => services.AddEntityFrameworkCouchbaseProvider();

    public void Validate(IDbContextOptions options)
    {
    }

    public DbContextOptionsExtensionInfo Info => _info ??= new CouchbaseExtensionInfo(this);

    //TODO use RelationalExtensionInfo as an example for implementation JM
    private sealed class CouchbaseExtensionInfo : DbContextOptionsExtensionInfo
    {
        private int? _serviceProviderHash;

        public CouchbaseExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override int GetServiceProviderHashCode()
        {
            if (_serviceProviderHash == null)
            {
                var hashCode = new HashCode();
                hashCode.Add(0);//TODO this may need to be removed when below TODO is resolved

                //TODO: Add to hashCode each option property.  Example: https://github.com/npgsql/efcore.pg/blob/main/src/EFCore.PG/Infrastructure/Internal/NpgsqlOptionsExtension.cs#L414
                //  hashCode.Add(Extension.PostgresVersion);

                _serviceProviderHash = hashCode.ToHashCode();
            }

            return _serviceProviderHash.Value;

        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            throw new NotImplementedException();
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            //TODO: Add to debugInfo each option property as a string of its GetHashCode() value.  Example: https://github.com/npgsql/efcore.pg/blob/main/src/EFCore.PG/Infrastructure/Internal/NpgsqlOptionsExtension.cs#L433
            //  debugInfo["ServantSoftware.EntityFrameworkCore.SampleProvider:" + nameof(NpgsqlDbContextOptionsBuilder.SetPostgresVersion)]
            //    = (Extension.PostgresVersion?.GetHashCode() ?? 0).ToString(CultureInfo.InvariantCulture);
        }

        public override bool IsDatabaseProvider { get; }
        
        public override string LogFragment { get; }
    }
}