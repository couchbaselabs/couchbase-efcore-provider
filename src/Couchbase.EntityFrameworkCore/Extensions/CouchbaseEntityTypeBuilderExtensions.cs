using System.Runtime.CompilerServices;
using System.Text;
using Couchbase.Protostellar.Admin.Collection.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseEntityTypeBuilderExtensions
{
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string bucket, string? scope = null, string? collection = null) where TEntity : class
    {
        var contextIdBuilder = new StringBuilder();
        contextIdBuilder.Append(bucket);
        contextIdBuilder.Append(".");
        contextIdBuilder.Append(scope ?? "_default");
        contextIdBuilder.Append('.');
        contextIdBuilder.Append(collection ?? "_default");
        entityTypeBuilder.ToTable(contextIdBuilder.ToString());
        
        return entityTypeBuilder;
    }
}