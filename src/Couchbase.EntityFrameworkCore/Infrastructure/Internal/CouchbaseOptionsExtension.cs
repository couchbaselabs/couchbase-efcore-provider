using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Couchbase;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

//TODO potentially implement IDbContextOptionsExtension instead of deriving from RelationalOptionsExtension
public class CouchbaseOptionsExtension<TNamedBucketProvider> : RelationalOptionsExtension where TNamedBucketProvider : class, INamedBucketProvider
{
    private readonly ClusterOptions _clusterOptions;
    private readonly CouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private CouchbaseOptionsExtensionInfo? _info;

    public CouchbaseOptionsExtension(ClusterOptions clusterOptions, CouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _clusterOptions = clusterOptions;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }
    protected internal CouchbaseOptionsExtension(CouchbaseOptionsExtension<TNamedBucketProvider> copyFrom)
        : base(copyFrom)
    {
    }

    public ClusterOptions ClusterOptions => _clusterOptions;

    public override string? ConnectionString => _clusterOptions.ConnectionString;

    public override DbContextOptionsExtensionInfo Info => _info ??= new CouchbaseOptionsExtensionInfo(this);

    public CouchbaseDbContextOptionsBuilder DbContextOptionsBuilder => _couchbaseDbContextOptionsBuilder;

    public override void ApplyServices(IServiceCollection services)
    {
        services.AddEntityFrameworkCouchbaseProvider(this);
    }

    private TNamedBucketProvider BucketProvider;
    
    public override void Validate(IDbContextOptions options)
    {
        // You can add any validation logic here, if necessary.
    }

    protected override RelationalOptionsExtension Clone() => new CouchbaseOptionsExtension<TNamedBucketProvider>(this);


    public class CouchbaseOptionsExtensionInfo : DbContextOptionsExtensionInfo
    {
        private readonly ClusterOptions _clusterOptions;

        public CouchbaseOptionsExtensionInfo(CouchbaseOptionsExtension<TNamedBucketProvider> extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => true;

        public override string LogFragment => $"Using Custom SQLite Provider - ConnectionString: {ConnectionString}";

        public override int GetServiceProviderHashCode() => ConnectionString.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is CouchbaseOptionsExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Couchbase:ConnectionString"] = ConnectionString;
        }

        public override CouchbaseOptionsExtension<TNamedBucketProvider> Extension => (CouchbaseOptionsExtension<TNamedBucketProvider>)base.Extension;
        private string? ConnectionString => Extension.Connection == null ?
            Extension.ConnectionString :
            Extension.Connection.ConnectionString;
    }
}