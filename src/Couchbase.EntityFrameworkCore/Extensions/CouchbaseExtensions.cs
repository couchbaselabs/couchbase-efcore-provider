using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Primitives;
using Couchbase.Core.Utils;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class CouchbaseExtensions
{
    public static EntityTypeBuilder<TEntity> ToCouchbaseCollection<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder, string bucket, string scope, string collection) where TEntity : class
    {
        var contextId = new StringBuilder();
        contextId.Append(bucket.EscapeIfRequired());
        contextId.Append('.');
        contextId.Append(scope.EscapeIfRequired());
        contextId.Append('.');
        contextId.Append(collection.EscapeIfRequired());
        return entityTypeBuilder.ToTable(contextId.ToString());
    }
}