using Couchbase.EntityFrameworkCore.Diagnostics.Internal;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.Metadata.Conventions;
using Couchbase.EntityFrameworkCore.Migrations.Internal;
using Couchbase.EntityFrameworkCore.Query;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseServiceCollectionExtensions
{
    public static IServiceCollection AddCouchbase<TContext, TNamedBucketProvider>(
        this IServiceCollection serviceCollection,
        ClusterOptions clusterOptions,
        Action<ICouchbaseDbContextOptionsBuilder>? couchbaseOptionsAction = null,
        Action<DbContextOptionsBuilder>? optionsAction = null)
        where TContext : DbContext
        => serviceCollection.AddDbContext<TContext>((_, options) =>
        {
            optionsAction?.Invoke(options);
            options.UseCouchbase<TNamedBucketProvider>(clusterOptions, couchbaseOptionsAction);
        });

    public static IServiceCollection AddEntityFrameworkCouchbaseProvider<TNamedBucketProvider>(this IServiceCollection serviceCollection,
        CouchbaseOptionsExtension<TNamedBucketProvider> optionsExtension, string bucketName) where TNamedBucketProvider : class, INamedBucketProvider
    {
        serviceCollection.AddCouchbase(options =>
        {
            options.WithConnectionString(optionsExtension.ClusterOptions.ConnectionString);
            options.WithCredentials(optionsExtension.ClusterOptions.UserName, optionsExtension.ClusterOptions.Password);
        });
        
        serviceCollection.AddCouchbaseBucket<TNamedBucketProvider>(bucketName);
        serviceCollection.AddLogging(); //this should be injectible from the app side
        
        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IRelationalTypeMappingSource, CouchbaseTypeMappingSource>()
            .TryAdd<IDatabase, CouchbaseDatabaseWrapper>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<CouchbaseOptionsExtension<TNamedBucketProvider>>>()
            .TryAdd<LoggingDefinitions, CouchbaseLoggingDefinitions>()
            .TryAdd<IModificationCommandBatchFactory, CouchbaseModificationCommandBatchFactory>()
            .TryAdd<IUpdateSqlGenerator, CouchbaseUpdateSqlGenerator>()
            .TryAdd<IAsyncQueryProvider, CouchbaseQueryProvider>()
            .TryAdd<IQuerySqlGeneratorFactory, CouchbaseQuerySqlGeneratorFactory>()
            .TryAdd<ISqlGenerationHelper, CouchbaseSqlGenerationHelper>()
            .TryAdd<IShapedQueryCompilingExpressionVisitorFactory, CouchbaseShapedQueryCompilingExpressionVisitorFactory>()
            .TryAdd<IHistoryRepository, CouchbaseHistoryRepository>()//not used but required by ASP.NET
            
            .TryAdd<IModificationCommandBatchFactory, CouchbaseModificationCommandBatchFactory>()
            
            //Found that this was necessary, because the default convention of determining a
            //Model's primary key automatically based off of properties that have 'Id' in their
            //name was getting ignored.
            .TryAdd<IProviderConventionSetBuilder, CouchbaseConventionSetBuilder>()

            .TryAddProviderSpecificServices(m => m
                .TryAddScoped<QuerySqlGenerator, CouchbaseQuerySqlGenerator>()
                .TryAddScoped<ICouchbaseConnection, CouchbaseConnection>()
                .TryAddScoped<IQueryProvider, CouchbaseQueryProvider>()
                .TryAddScoped<IRelationalCommand, CouchbaseCommand>()
                .TryAddScoped<QueryContext, RelationalQueryContext>()
                .TryAddScoped<ICouchbaseClientWrapper, CouchbaseClientWrapper>()
            )

            .TryAdd<IRelationalConnection>(p => p.GetService<ICouchbaseConnection>());


        builder.TryAddCoreServices();

        serviceCollection
           // .AddScoped<IRelationalConnection, CouchbaseConnection>()
           //.AddScoped<IQueryCompiler, CouchbaseQueryCompiler>()
            .AddSingleton<ISqlGenerationHelper, CouchbaseSqlGenerationHelper>()
            .AddScoped<IRelationalDatabaseCreator, CouchbaseDatabaseCreator>();

        return serviceCollection;

    }
}