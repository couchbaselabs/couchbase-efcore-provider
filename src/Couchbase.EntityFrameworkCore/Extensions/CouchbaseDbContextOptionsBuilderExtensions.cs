using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseCouchbaseProvider(
        this DbContextOptionsBuilder optionsBuilder,
        ClusterOptions clusterOptions,
        Action<CouchbaseDbContextOptionsBuilder>? couchbaseOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder, clusterOptions));
        ConfigureWarnings(optionsBuilder);
        couchbaseOptionsAction?.Invoke(new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions));

        return optionsBuilder;
    }

    internal static CouchbaseOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder,
        ClusterOptions clusterOptions)
        => optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension>()
           ?? new CouchbaseOptionsExtension().WithClusterOptions(clusterOptions);

    internal static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                                   ?? new CoreOptionsExtension();
        
        //TODO make this CouchbaseDbOptionsExtension JM
      //  coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}