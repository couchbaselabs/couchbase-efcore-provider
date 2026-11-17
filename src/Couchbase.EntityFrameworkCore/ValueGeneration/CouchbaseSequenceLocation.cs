using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Where a property's Couchbase sequence lives: <c>bucket.scope.name</c>.
/// </summary>
/// <remarks>
/// The one place that resolves a sequence's location, so value generation, <c>EnsureCreated</c>
/// and <c>EnsureDeleted</c> always agree. The bucket is the bucket of the entity type that declares
/// the property (a <c>Bucket.Scope.Collection</c> table name names it; otherwise the context's
/// bucket). The scope is the property's scope override, or else the context's scope.
/// </remarks>
internal readonly record struct CouchbaseSequenceLocation(string Bucket, string Scope, string Name)
{
    /// <summary>
    /// The location of <paramref name="property"/>'s sequence, or <see langword="null"/> when the
    /// property does not use one.
    /// </summary>
    public static CouchbaseSequenceLocation? Find(
        IProperty property, string configuredBucket, string configuredScope)
    {
        if (property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation)?.Value
            is not string { Length: > 0 } name)
        {
            return null;
        }

        var tableName = (property.DeclaringType as IEntityType)?.GetTableName();
        var bucket = CouchbaseKeyspace.ResolveBucket(tableName, configuredBucket);
        var scope = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation)?.Value as string
            ?? configuredScope;

        return new CouchbaseSequenceLocation(bucket, scope, name);
    }

    /// <summary>The delimited <c>bucket.scope.name</c> reference for a SQL++ statement.</summary>
    public string ToSql(ISqlGenerationHelper sqlGenerationHelper)
        => $"{sqlGenerationHelper.DelimitIdentifier(Bucket)}.{sqlGenerationHelper.DelimitIdentifier(Scope)}."
            + sqlGenerationHelper.DelimitIdentifier(Name);
}
