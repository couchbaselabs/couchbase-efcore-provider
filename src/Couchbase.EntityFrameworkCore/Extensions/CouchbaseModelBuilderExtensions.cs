// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseModelBuilderExtensions
{
    /// <summary>
    /// Configures all entities in the model with Couchbase keyspace settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For each entity that doesn't already have a full keyspace (Bucket.Scope.Collection),
    /// this method constructs the full keyspace using:
    /// <list type="bullet">
    /// <item>Bucket: Always from DbContext configuration</item>
    /// <item>Scope: From entity's <see cref="CouchbaseKeyspaceAttribute"/> if specified with scope override,
    /// otherwise from DbContext configuration</item>
    /// <item>Collection: From the entity's table name (set via attribute or fluent API)</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="dbContext">The DbContext to get bucket/scope configuration from.</param>
    /// <param name="toLowerCaseNaming">If true, converts collection names to lowercase.</param>
    /// <returns>The model builder for chaining.</returns>
    public static ModelBuilder ConfigureToCouchbase(
            this ModelBuilder modelBuilder, 
            DbContext dbContext, 
            bool? toLowerCaseNaming = null)
    {
        var dbContextOptions = (ICouchbaseDbContextOptionsBuilder)dbContext.
            Database.GetInfrastructure().GetService(typeof(ICouchbaseDbContextOptionsBuilder))!;

        ArgumentException.ThrowIfNullOrEmpty(dbContextOptions.Bucket, "DbContext Bucket configuration");
        ArgumentException.ThrowIfNullOrEmpty(dbContextOptions.Scope, "DbContext Scope configuration");

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Skip owned types - they are embedded in their owner's document
            if (entityType.IsOwned()) continue;

            var tableName = entityType.GetTableName();
            if (tableName is null) continue;

            var toLowerCase = toLowerCaseNaming is true;

            // Already a full keyspace (Bucket.Scope.Collection): the entity was mapped to an
            // explicit bucket/scope (e.g. ToCouchbaseCollection(bucket, scope, collection)).
            // Bucket and scope are case-sensitive in Couchbase, so never touch them here — only
            // optionally normalize the collection segment to match the bare-collection path.
            if (CouchbaseKeyspace.TryParse(tableName, out var existingKeyspace))
            {
                if (toLowerCase)
                {
                    var collection = existingKeyspace!.Value.Collection.ToLowerInvariant();
                    if (collection != existingKeyspace.Value.Collection)
                    {
                        entityType.SetTableName(new CouchbaseKeyspace(
                            existingKeyspace.Value.Bucket, existingKeyspace.Value.Scope, collection).ToString());
                    }
                }
                continue;
            }

            // tableName is just the collection name; lowercase it (if requested), then compose
            // the full keyspace from the DbContext-configured bucket and scope.
            if (toLowerCase)
            {
                tableName = tableName.ToLowerInvariant();
            }

            // Check for scope override annotation (set by CouchbaseKeyspaceAttribute with scope)
            var scopeOverride = entityType.FindAnnotation(CouchbaseKeyspaceConvention.ScopeOverrideAnnotation)?.Value as string;
            var scope = scopeOverride ?? dbContextOptions.Scope;

            var keyspace = new CouchbaseKeyspace(dbContextOptions.Bucket, scope, tableName);
            entityType.SetTableName(keyspace.ToString());
        }
        return modelBuilder;
    }
}
