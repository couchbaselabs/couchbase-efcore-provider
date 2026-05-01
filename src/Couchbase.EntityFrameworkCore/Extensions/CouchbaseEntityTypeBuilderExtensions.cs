using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseEntityTypeBuilderExtensions
{
    /// <summary>
    /// Maps an entity to a Couchbase Collection. The Bucket name and Scope name come from the provider initialization.
    /// </summary>
    /// <param name="entityTypeBuilder">The entity type builder.</param>
    /// <param name="context">The DbContext to get bucket/scope configuration from.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <returns>The entity type builder for chaining.</returns>
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        DbContext context,
        string collectionName) where TEntity : class
    {
        var dbContextOptions = (ICouchbaseDbContextOptionsBuilder)context.
            Database.GetInfrastructure().GetService(typeof(ICouchbaseDbContextOptionsBuilder))!;

        var keyspace = new CouchbaseKeyspace(dbContextOptions.Bucket, dbContextOptions.Scope, collectionName);
        return entityTypeBuilder.ToTable(keyspace.ToString());
    }

    /// <summary>
    /// Maps an entity to a Couchbase Collection with a specific scope. The Bucket name comes from the provider initialization.
    /// </summary>
    /// <param name="entityTypeBuilder">The entity type builder.</param>
    /// <param name="context">The DbContext to get bucket configuration from.</param>
    /// <param name="scope">The scope name.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <returns>The entity type builder for chaining.</returns>
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder, DbContext context, string scope, string collectionName) where TEntity : class
    {
        var dbContextOptions = (ICouchbaseDbContextOptionsBuilder)context.
            Database.GetInfrastructure().GetService(typeof(ICouchbaseDbContextOptionsBuilder))!;

        var keyspace = new CouchbaseKeyspace(dbContextOptions.Bucket, scope, collectionName);
        return entityTypeBuilder.ToTable(keyspace.ToString());
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
