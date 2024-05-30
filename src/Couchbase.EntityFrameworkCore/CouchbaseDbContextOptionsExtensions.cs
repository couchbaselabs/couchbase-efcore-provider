using System.Data.Common;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
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
  private static CouchbaseOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder options)
  {
    return options.Options.FindExtension<CouchbaseOptionsExtension>() ?? new CouchbaseOptionsExtension();
  }

  private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
  {
    CoreOptionsExtension extension = RelationalOptionsExtension.WithDefaultWarningConfiguration(optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension());
    ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension<CoreOptionsExtension>(extension);
  }
}