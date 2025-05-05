// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

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
                keyspaceBuilder.Append(tableName);
                keyspaceBuilder.Append('.');
                keyspaceBuilder.Append(dbContextOptions.Bucket);
                keyspaceBuilder.Append('.');
                keyspaceBuilder.Append(dbContextOptions.Scope);

                entityType.SetTableName(keyspaceBuilder.ToString());
            }
        }
        return modelBuilder;
    }
}
