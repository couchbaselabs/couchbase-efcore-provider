namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Maps an entity to a Scope and Collection. The Bucket name is pulled from the 
/// DbContext configuration that is injected via DI.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute to specify the collection (and optionally scope) where an entity's
/// documents are stored. The full keyspace (Bucket.Scope.Collection) is constructed at
/// runtime by combining this with the bucket from <see cref="Infrastructure.ICouchbaseDbContextOptionsBuilder"/>.
/// </para>
/// <para>
/// If only a collection is specified, the scope from the DbContext configuration is used.
/// If both scope and collection are specified, the scope from this attribute overrides
/// the DbContext-level scope for this entity.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Uses DbContext-level scope
/// [CouchbaseKeyspace("users")]
/// public class User { }
///
/// // Overrides to use "analytics" scope instead of DbContext-level scope
/// [CouchbaseKeyspace("analytics", "metrics")]
/// public class Metric { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public class CouchbaseKeyspaceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified collection name.
    /// The scope will be inherited from the DbContext configuration.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    public CouchbaseKeyspaceAttribute(string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(collection);
        Collection = collection;
        HasScopeOverride = false;
    }

    /// <summary>
    /// Initializes a new instance with the specified scope and collection names.
    /// The scope specified here overrides the DbContext-level scope for this entity.
    /// </summary>
    /// <param name="scope">The scope name (overrides DbContext-level scope).</param>
    /// <param name="collection">The collection name.</param>
    public CouchbaseKeyspaceAttribute(string scope, string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(collection);
        Scope = scope;
        Collection = collection;
        HasScopeOverride = true;
    }

    /// <summary>
    /// Initializes a new instance with an explicit bucket, scope, and collection.
    /// Use this to map an entity to a bucket other than the DbContext's configured bucket
    /// (a single DbContext may span multiple buckets on the same cluster).
    /// </summary>
    /// <param name="bucket">The bucket name (overrides the DbContext-level bucket).</param>
    /// <param name="scope">The scope name.</param>
    /// <param name="collection">The collection name.</param>
    public CouchbaseKeyspaceAttribute(string bucket, string scope, string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucket);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(collection);
        Bucket = bucket;
        Scope = scope;
        Collection = collection;
        HasScopeOverride = true;
    }

    /// <summary>
    /// Gets the bucket name, or <c>null</c> if the bucket should be inherited from DbContext configuration.
    /// </summary>
    public string? Bucket { get; }

    /// <summary>
    /// Gets the scope name, or <c>null</c> if the scope should be inherited from DbContext configuration.
    /// </summary>
    public string? Scope { get; }

    /// <summary>
    /// Gets the collection name.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets a value indicating whether this attribute explicitly overrides the scope.
    /// When <c>false</c>, the scope from DbContext configuration should be used.
    /// </summary>
    public bool HasScopeOverride { get; }
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
