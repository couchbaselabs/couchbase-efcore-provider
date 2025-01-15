using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseCouchbaseProvider(
        this CouchbaseDbContextOptionsBuilder couchbaseDbContextOptions,
        ClusterOptions clusterOptions,
        Action<CouchbaseDbContextOptionsBuilder>? couchbaseOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(couchbaseDbContextOptions);

        var couchbaseDbContextOptionsBuilder = new CouchbaseDbContextOptionsBuilder(couchbaseDbContextOptions.OptionsBuilder, clusterOptions);
        couchbaseOptionsAction?.Invoke(couchbaseDbContextOptionsBuilder);

        var extension = GetOrCreateExtension(couchbaseDbContextOptions.OptionsBuilder, clusterOptions, couchbaseDbContextOptionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)couchbaseDbContextOptions).AddOrUpdateExtension(extension);
        ConfigureWarnings(couchbaseDbContextOptions.OptionsBuilder);

        return couchbaseDbContextOptions.OptionsBuilder;
    }

    public static DbContextOptionsBuilder UseCouchbaseProvider(
        this DbContextOptionsBuilder optionsBuilder,
        ClusterOptions clusterOptions,
        Action<CouchbaseDbContextOptionsBuilder>? couchbaseOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var couchbaseDbContextOptionsBuilder = new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions);
        couchbaseOptionsAction?.Invoke(couchbaseDbContextOptionsBuilder);

        var extension = GetOrCreateExtension(optionsBuilder, clusterOptions, couchbaseDbContextOptionsBuilder);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        ConfigureWarnings(optionsBuilder);

        return optionsBuilder;
    }

    internal static CouchbaseOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder,
        ClusterOptions clusterOptions, CouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
        => optionsBuilder.Options.FindExtension<CouchbaseOptionsExtension>()
            is CouchbaseOptionsExtension existing
            ? new CouchbaseOptionsExtension(existing)
            : new CouchbaseOptionsExtension(couchbaseDbContextOptionsBuilder);

    internal static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                                    ?? new CoreOptionsExtension();

        coreOptionsExtension = coreOptionsExtension.WithWarningsConfiguration(
            coreOptionsExtension.WarningsConfiguration.TryWithExplicit(
                RelationalEventId.AmbientTransactionWarning, WarningBehavior.Throw));

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}