using System.Runtime.CompilerServices;
using System.Text;
using Couchbase.Protostellar.Admin.Collection.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseEntityTypeBuilderExtensions
{
    /// <summary>
    /// Maps an entity to a Scope and Collection. The other part of the keyspace,
    /// the Bucket name, is pulled from the ClusterOptions that is injected via DI
    /// </summary>
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder, string? scope = null, string? collection = null) where TEntity : class
    {
        var keyspace = new StringBuilder();
        keyspace.Append(scope ?? "_default");
        keyspace.Append('.');
        keyspace.Append(collection ?? "_default");
        entityTypeBuilder.ToTable(keyspace.ToString());

        return entityTypeBuilder;
    }
}