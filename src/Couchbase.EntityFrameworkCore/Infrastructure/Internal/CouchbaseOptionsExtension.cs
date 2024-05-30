using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Couchbase;
using Couchbase.EntityFrameworkCore.Extensions;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseOptionsExtension : IDbContextOptionsExtension
{
    private ClusterOptions? _clusterOptions;
    private DbContextOptionsExtensionInfo? _info;
    
    public CouchbaseOptionsExtension()
    {
    }

    public CouchbaseOptionsExtension(CouchbaseOptionsExtension copyFrom)
    {
        _clusterOptions = copyFrom._clusterOptions;
    }
    
    public CouchbaseOptionsExtension(ClusterOptions clusterOptions)
    {
        _clusterOptions = clusterOptions;
    }

    public CouchbaseOptionsExtension WithClusterOptions(ClusterOptions clusterOptions)
    {
        var clone = Clone();
        clone._clusterOptions = clusterOptions;
        return clone;
    }
    
    private CouchbaseOptionsExtension Clone() => new(this);
    
    public void ApplyServices(IServiceCollection services) => services.AddEntityFrameworkCouchbaseProvider(this);

    public void Validate(IDbContextOptions options)
    {
    }
    
    internal ClusterOptions ClusterOptions => _clusterOptions;

    public DbContextOptionsExtensionInfo Info => _info ??= new CouchbaseExtensionInfo(this);

    //TODO use RelationalExtensionInfo as an example for implementation JM
    private sealed class CouchbaseExtensionInfo : DbContextOptionsExtensionInfo
    {
        private int? _serviceProviderHash;

        public CouchbaseExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        private new CouchbaseOptionsExtension Extension => (CouchbaseOptionsExtension)base.Extension;

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

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is CouchbaseExtensionInfo otherInfo
            && Extension._clusterOptions == otherInfo.Extension._clusterOptions;
        
        

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