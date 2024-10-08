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
      Action<CouchbaseDbContextOptionsBuilder>? couchbaseActionOptions = null) where TNamedBucketProvider : class, INamedBucketProvider
  {
    var couchbaseDbContextOptionsBuilder = new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions);
    couchbaseActionOptions?.Invoke(couchbaseDbContextOptionsBuilder);

    var extension = CouchbaseDbContextOptionsBuilderExtensions.GetOrCreateExtension<TNamedBucketProvider>(optionsBuilder, clusterOptions, couchbaseDbContextOptionsBuilder);
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
    CouchbaseDbContextOptionsBuilderExtensions.ConfigureWarnings(optionsBuilder);

    return optionsBuilder;
  }
  
  #nullable disable
  private static CouchbaseOptionsExtension<TNamedBucketProvider> GetOrCreateExtension<TNamedBucketProvider>(DbContextOptionsBuilder optionsBuilder, ClusterOptions clusterOptions, CouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder) where TNamedBucketProvider : class, INamedBucketProvider
    => optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension<TNamedBucketProvider>>() is CouchbaseOptionsExtension<TNamedBucketProvider> existing
      ? new CouchbaseOptionsExtension<TNamedBucketProvider>(existing)
      : new CouchbaseOptionsExtension<TNamedBucketProvider>(clusterOptions, couchbaseDbContextOptionsBuilder);

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