using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore;

public static class CouchbaseDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder<TContext> UseCouchbase<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder, 
        ICluster cluster,
        Action<ICouchbaseDbContextOptionsBuilder>? optionsAction = null) 
        where TContext : DbContext 
        => (DbContextOptionsBuilder<TContext>)UseCouchbaseDB(
        optionsBuilder,
        cluster,
        optionsAction);

    public static DbContextOptionsBuilder UseCouchbaseDB(this DbContextOptionsBuilder optionsBuilder,
        ICluster cluster, Action<ICouchbaseDbContextOptionsBuilder>? optionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(cluster);

        var extension = (optionsBuilder.Options.FindExtension<CouchbaseDbOptionsExtension>()
                         ?? new CouchbaseDbOptionsExtension())
            .WithCluster(cluster);
        
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        optionsAction?.Invoke(new CouchbaseDbContextOptionsBuilder(optionsBuilder));
        return optionsBuilder;
    }
}