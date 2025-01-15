// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ç;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestHelpers : RelationalTestHelpers
{
    protected CouchbaseTestHelpers()
    {
    }

    public static CouchbaseTestHelpers Instance { get; } = new();

    public override IServiceCollection AddProviderServices(IServiceCollection services)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter(level => level >= LogLevel.Debug);
                builder.AddFile("Logs/myapp-{Date}.txt", LogLevel.Debug);
            });

        return services.AddEntityFrameworkCouchbase(
            new CouchbaseOptionsExtension(
                new CouchbaseDbContextOptionsBuilder(
                    new DbContextOptionsBuilder(),
                    new ClusterOptions()
                        .WithLogging(loggerFactory)
                        .WithConnectionString(TestEnvironment.ConnectionString)
                        .WithCredentials(TestEnvironment.Username, TestEnvironment.Password))));
    }

    public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
    {
        return optionsBuilder.UseCouchbase(
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

    public override LoggingDefinitions LoggingDefinitions { get; } = new CouchbaseLoggingDefinitions();
}
