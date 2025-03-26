using System.Runtime.CompilerServices;
using System.Text;
using Couchbase.Core.Utils;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Protostellar.Admin.Collection.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseEntityTypeBuilderExtensions
{
    /// <summary>
    /// Maps an entity to a Couchbase Collection. The Bucket name and Scope name come from the provider initialization.
    /// </summary>
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,  string collection) where TEntity : class
    {
        return entityTypeBuilder.ToTable(collection);
    }

    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        DbContext context,
        string collectionName) where TEntity : class
    {
        var dbContextOptions = (ICouchbaseDbContextOptionsBuilder)context.
            Database.GetInfrastructure().GetService(typeof(ICouchbaseDbContextOptionsBuilder))!;

        var keyspaceBuilder = new StringBuilder();
        keyspaceBuilder.Append(collectionName);
        keyspaceBuilder.Append('.');
        keyspaceBuilder.Append(dbContextOptions.Bucket);
        keyspaceBuilder.Append('.');
        keyspaceBuilder.Append(dbContextOptions.Scope);

        return entityTypeBuilder.ToTable(keyspaceBuilder.ToString());
    }

    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder, string scope, string collection) where TEntity : class
    {
        return  entityTypeBuilder.ToTable($"{collection}.{scope}");
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2021 Couchbase, Inc.
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
