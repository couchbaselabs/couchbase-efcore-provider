using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
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
    var couchbaseDbContextOptionsBuilder = new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions);
    couchbaseActionOptions?.Invoke(couchbaseDbContextOptionsBuilder);

    var extension = CouchbaseDbContextOptionsBuilderExtensions.GetOrCreateExtension(optionsBuilder, clusterOptions, couchbaseDbContextOptionsBuilder);
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
    CouchbaseDbContextOptionsBuilderExtensions.ConfigureWarnings(optionsBuilder);
    AddSaveChangesInterceptor(optionsBuilder);

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
    AddSaveChangesInterceptor(optionsBuilder);

    return optionsBuilder;
  }
  
  private static void AddSaveChangesInterceptor(DbContextOptionsBuilder optionsBuilder)
  {
    var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                               ?? new CoreOptionsExtension();

    // Check if interceptor is already added
    var existingInterceptors = coreOptionsExtension.Interceptors ?? Enumerable.Empty<IInterceptor>();
    if (existingInterceptors.OfType<CouchbaseSaveChangesInterceptor>().Any())
    {
      return;
    }

    // Add the interceptor
    var interceptor = new CouchbaseSaveChangesInterceptor();
    coreOptionsExtension = coreOptionsExtension.WithInterceptors(
      existingInterceptors.Append(interceptor).ToArray());

    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
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