// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.Common;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestStore : RelationalTestStore
{
    public const int CommandTimeout = 30;

    public static CouchbaseTestStore GetOrCreate(string name, bool sharedCache = false)
        => new(name, sharedCache: sharedCache);

    public static CouchbaseTestStore GetOrCreateInitialized(string name)
    {
        return new CouchbaseTestStore(name).InitializeSqlite(
            new ServiceCollection().AddEntityFrameworkCouchbase(
                    new CouchbaseOptionsExtension(
                        new CouchbaseDbContextOptionsBuilder(
                            new DbContextOptionsBuilder(),
                            new ClusterOptions()
                                .WithLogging(LoggerFactory.Create(builder =>
                                        {
                                            builder.AddFilter(level => level >= LogLevel.Debug);
                                            builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
                                        }))
                                .WithConnectionString(TestEnvironment.ConnectionString)
                                .WithCredentials(TestEnvironment.Username, TestEnvironment.Password))))
                .BuildServiceProvider(validateScopes: true),
            (Func<DbContext>)null,
            null);
    }

    public static CouchbaseTestStore GetExisting(string name)
        => new(name, seed: false);

    public static CouchbaseTestStore Create(string name)
        => new(name, shared: false);

    private readonly bool _seed;

    private CouchbaseTestStore(string name, bool seed = true, bool sharedCache = false, bool shared = true)
        : base(name, shared)
    {
        _seed = seed;

        ConnectionString = TestEnvironment.ConnectionString;

        var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter(level => level >= LogLevel.Debug);
                builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
            });

        var dbConnectionOptions = new CouchbaseDbContextOptionsBuilder(
            new DbContextOptionsBuilder(),
            new ClusterOptions()
                .WithLogging(loggerFactory)
                .WithConnectionString(TestEnvironment.ConnectionString)
                .WithCredentials(TestEnvironment.Username, TestEnvironment.Password));

        var services = new ServiceCollection();
        var extension = new CouchbaseOptionsExtension(dbConnectionOptions);
        extension.ApplyServices(services);
        services.AddEntityFrameworkCouchbase(new CouchbaseOptionsExtension(dbConnectionOptions));

        ServiceProvider = services.BuildServiceProvider();
        var connection = new CouchbaseConnection( this.ServiceProvider, dbConnectionOptions);
        Connection = connection;
    }

    public virtual DbContextOptionsBuilder AddProviderOptions(
        DbContextOptionsBuilder builder,
        Action<CouchbaseDbContextOptionsBuilder> configureSqlite)
    {
        return builder.UseCouchbase(
            new ClusterOptions()
                .WithCredentials("Administrator", "password")
                .WithConnectionString("couchbase://localhost")
                .WithLogging(
                    LoggerFactory.Create(
                        builder =>
                            {
                                builder.AddFilter(level => level >= LogLevel.Debug);
                                builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
                            })),
            couchbaseDbContextOptions =>
                {
                    couchbaseDbContextOptions.Bucket = "Content";
                    couchbaseDbContextOptions.Scope = "Blogs";
                });
    }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => AddProviderOptions(builder, configureSqlite: null);

    public CouchbaseTestStore InitializeSqlite(IServiceProvider serviceProvider, Func<DbContext> createContext, Action<DbContext> seed)
        => (CouchbaseTestStore)Initialize(serviceProvider, createContext, seed);

    public CouchbaseTestStore InitializeSqlite(
        IServiceProvider serviceProvider,
        Func<CouchbaseTestStore, DbContext> createContext,
        Action<DbContext> seed)
        => (CouchbaseTestStore)Initialize(serviceProvider, () => createContext(this), seed);

    protected override void Initialize(Func<DbContext> createContext, Action<DbContext> seed, Action<DbContext> clean)
    {
        if (!_seed)
        {
            return;
        }

        using var context = createContext();
        if (!context.Database.EnsureCreated())
        {
            clean?.Invoke(context);
            Clean(context);
        }

        seed?.Invoke(context);
    }

    public override void Clean(DbContext context)
        => context.Database.EnsureClean();

    public int ExecuteNonQuery(string sql, params object[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return command.ExecuteNonQuery();
    }

    public T ExecuteScalar<T>(string sql, params object[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return (T)command.ExecuteScalar();
    }

    private DbCommand CreateCommand(string commandText, object[] parameters)
    {
        var command = (CouchbaseCommand)Connection.CreateCommand();

        command.CommandText = commandText;
        command.CommandTimeout = CommandTimeout;

        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue("@p" + i, parameters[i]);
        }

        return command;
    }
}
