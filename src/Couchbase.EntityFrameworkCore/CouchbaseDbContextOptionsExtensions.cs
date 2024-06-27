using System.Data.Common;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore;

public static class CouchbaseDbContextOptionsExtensions
{
  public static DbContextOptionsBuilder UseCouchbase(
      this DbContextOptionsBuilder optionsBuilder, 
      ClusterOptions clusterOptions,
      Action<CouchbaseDbContextOptionsBuilder>? couchbaseActionOptions = null)
  {
    var extension = CouchbaseDbContextOptionsBuilderExtensions.GetOrCreateExtension(optionsBuilder, clusterOptions);
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
    CouchbaseDbContextOptionsBuilderExtensions.ConfigureWarnings(optionsBuilder);  
    couchbaseActionOptions?.Invoke(new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions));
    return optionsBuilder;
  }
  

  #nullable disable
  private static CouchbaseOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder, ClusterOptions clusterOptions)
    => optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension>() is CouchbaseOptionsExtension existing
      ? new CouchbaseOptionsExtension(existing)
      : new CouchbaseOptionsExtension(clusterOptions);

  private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
  {
    var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                               ?? new CoreOptionsExtension();

    coreOptionsExtension = coreOptionsExtension.WithWarningsConfiguration(
      coreOptionsExtension.WarningsConfiguration.TryWithExplicit(
        RelationalEventId.AmbientTransactionWarning, WarningBehavior.Throw));

    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
  }
}