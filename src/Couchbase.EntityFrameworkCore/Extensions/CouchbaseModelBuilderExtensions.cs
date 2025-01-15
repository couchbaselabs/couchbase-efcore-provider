// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Couchbase.Core.Utils;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseModelBuilderExtensions
{
    public static ModelBuilder ConfigureToCouchbase(
        this ModelBuilder modelBuilder,
        DbContext dbContext)
    {
        var dbContextOptions = (ICouchbaseDbContextOptionsBuilder)dbContext.
            Database.GetInfrastructure().GetService(typeof(ICouchbaseDbContextOptionsBuilder))!;

        var dbSetNames = dbContext.GetType().GetProperties()
            .Where(x => x.PropertyType.IsGenericType && typeof(DbSet<>).IsAssignableFrom(x.PropertyType.GetGenericTypeDefinition()))
            .Select(x => x.Name)
            .ToList();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!entityType.IsOwned())
            {
                var tableName = entityType.GetTableName();
                if (tableName is null) continue;

                var splitTableName = tableName.Split('.');
                if(splitTableName.Length == 3) continue;

                var keyspaceBuilder = new StringBuilder();
                keyspaceBuilder.Append(dbContextOptions.Bucket);
                keyspaceBuilder.Append('.');
                keyspaceBuilder.Append(dbContextOptions.Scope);
                keyspaceBuilder.Append('.');
                keyspaceBuilder.Append(tableName);

                entityType.SetTableName(keyspaceBuilder.ToString());
            }
        }
        return modelBuilder;
    }
}
