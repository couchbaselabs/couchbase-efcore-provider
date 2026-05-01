// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
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
            if (toLowerCaseNaming.HasValue && toLowerCaseNaming.Value)
            {
                tableName = tableName.ToLower();
            }

            // Skip if already a full keyspace (Bucket.Scope.Collection)
            if (CouchbaseKeyspace.TryParse(tableName, out _)) continue;

            // tableName is just the collection name, add bucket and scope
            var keyspace = new CouchbaseKeyspace(dbContextOptions.Bucket, dbContextOptions.Scope, tableName);
            entityType.SetTableName(keyspace.ToString());
        }
        return modelBuilder;
    }
}
