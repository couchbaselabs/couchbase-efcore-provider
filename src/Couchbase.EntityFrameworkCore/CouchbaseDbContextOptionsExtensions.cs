using System.Data.Common;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore;

public static class CouchbaseDbContextOptionsExtensions
{
  public static DbContextOptionsBuilder UseCouchbase<TNamedBucketProvider>(
      this DbContextOptionsBuilder optionsBuilder, 
      ClusterOptions clusterOptions,
      Action<CouchbaseDbContextOptionsBuilder>? couchbaseActionOptions = null)
  {
    var extension = CouchbaseDbContextOptionsBuilderExtensions.GetOrCreateExtension<INamedBucketProvider>(optionsBuilder, clusterOptions);
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
    CouchbaseDbContextOptionsBuilderExtensions.ConfigureWarnings(optionsBuilder);  
    couchbaseActionOptions?.Invoke(new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions));
    return optionsBuilder;
  }
  
  #nullable disable
  private static CouchbaseOptionsExtension<TNamedBucketProvider> GetOrCreateExtension<TNamedBucketProvider>(DbContextOptionsBuilder optionsBuilder, ClusterOptions clusterOptions) where TNamedBucketProvider : class, INamedBucketProvider
    => optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension<TNamedBucketProvider>>() is CouchbaseOptionsExtension<TNamedBucketProvider> existing
      ? new CouchbaseOptionsExtension<TNamedBucketProvider>(existing)
      : new CouchbaseOptionsExtension<TNamedBucketProvider>(clusterOptions);

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