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
            DbContext dbContext, 
            bool? toLowerCaseNaming = null)
    {
        var dbContextOptions = (ICouchbaseDbContextOptionsBuilder)dbContext.
            Database.GetInfrastructure().GetService(typeof(ICouchbaseDbContextOptionsBuilder))!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Skip owned types - they are embedded in their owner's document
            if (entityType.IsOwned()) continue;

            var tableName = entityType.GetTableName();
            if (tableName is null) continue;
            if (toLowerCaseNaming.HasValue &&
                toLowerCaseNaming.Value)
            {
                tableName = tableName.ToLower();
            }

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
        return modelBuilder;
    }
}
