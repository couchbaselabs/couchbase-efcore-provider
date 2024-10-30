using System.Runtime.CompilerServices;
using System.Text;
using Couchbase.Core.Utils;
using Couchbase.Protostellar.Admin.Collection.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseEntityTypeBuilderExtensions
{
    /// <summary>
    /// Maps an entity to a Couchbase Collection. The Bucket name and Scope name come from the provider initialization.
    /// </summary>
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder, string collection) where TEntity : class
    {
        return entityTypeBuilder.ToTable(collection);
    }

    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder, string scope, string collection) where TEntity : class
    {
        return  entityTypeBuilder.ToTable($"{collection}.{scope}");
    }
}