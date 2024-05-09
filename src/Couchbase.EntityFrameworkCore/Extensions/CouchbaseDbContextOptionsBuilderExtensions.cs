using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseSampleProvider(
        this DbContextOptionsBuilder optionsBuilder,
        Action<CouchbaseDbContextOptionsBuilder>? couchbaseOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder));
        ConfigureWarnings(optionsBuilder);

        couchbaseOptionsAction?.Invoke(new CouchbaseDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    private static CouchbaseDbOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<CouchbaseDbOptionsExtension>()
           ?? new CouchbaseDbOptionsExtension().WithConnectionString(
               "couchbase://localhost");

    private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                                   ?? new CoreOptionsExtension();
        
        //TODO make this CouchbaseDbOptionsExtension JM
      //  coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}