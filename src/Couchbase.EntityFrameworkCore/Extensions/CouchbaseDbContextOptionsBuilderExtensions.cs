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

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
