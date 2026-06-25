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
      => optionsBuilder.UseCouchbase(clusterOptions, couchbaseActionOptions, applicationServiceProvider: null);

  // The applicationServiceProvider parameter is required (not optional) so this overload has a
  // distinct arity from the one above — preserving binary compatibility for consumers compiled
  // against the original 3-parameter signature, and avoiding overload ambiguity.
  public static DbContextOptionsBuilder UseCouchbase(
      this DbContextOptionsBuilder optionsBuilder,
      ClusterOptions clusterOptions,
      Action<CouchbaseDbContextOptionsBuilder>? couchbaseActionOptions,
      IServiceProvider? applicationServiceProvider)
  {
    var couchbaseDbContextOptionsBuilder = new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions);
    couchbaseActionOptions?.Invoke(couchbaseDbContextOptionsBuilder);
    // Captured when configured through AddCouchbase<TContext>; enables resolving an
    // application-registered shared cluster (one Cluster per server across buckets).
    couchbaseDbContextOptionsBuilder.ApplicationServiceProvider = applicationServiceProvider;

    var extension = CouchbaseDbContextOptionsBuilderExtensions.GetOrCreateExtension(optionsBuilder, clusterOptions, couchbaseDbContextOptionsBuilder);
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
    CouchbaseDbContextOptionsBuilderExtensions.ConfigureWarnings(optionsBuilder);
    CouchbaseDbContextOptionsBuilderExtensions.AddSaveChangesInterceptor(optionsBuilder);

    return optionsBuilder;
  }

  public static DbContextOptionsBuilder UseCouchbase(
    this DbContextOptionsBuilder optionsBuilder,
    string connectionString,
    Action<CouchbaseDbContextOptionsBuilder>? couchbaseActionOptions = null)
  {
    var clusterOptions = new ClusterOptions().WithConnectionString(connectionString);
    var couchbaseDbContextOptionsBuilder = new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions);
    couchbaseActionOptions?.Invoke(couchbaseDbContextOptionsBuilder);

    var extension = CouchbaseDbContextOptionsBuilderExtensions.GetOrCreateExtension(optionsBuilder, clusterOptions, couchbaseDbContextOptionsBuilder);
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
    CouchbaseDbContextOptionsBuilderExtensions.ConfigureWarnings(optionsBuilder);
    CouchbaseDbContextOptionsBuilderExtensions.AddSaveChangesInterceptor(optionsBuilder);

    return optionsBuilder;
  }
  
  #nullable disable
  private static CouchbaseOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder, ClusterOptions clusterOptions, CouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    => optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension>() is CouchbaseOptionsExtension existing
      ? new CouchbaseOptionsExtension(existing)
      : new CouchbaseOptionsExtension(couchbaseDbContextOptionsBuilder);

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