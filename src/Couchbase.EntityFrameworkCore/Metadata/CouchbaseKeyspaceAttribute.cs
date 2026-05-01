namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Maps an entity to a Scope and Collection. The Bucket name is pulled from the 
/// DbContext configuration that is injected via DI.
/// </summary>
/// <remarks>
/// Use this attribute to specify the collection (and optionally scope) where an entity's
/// documents are stored. The full keyspace (Bucket.Scope.Collection) is constructed at
/// runtime by combining this with the bucket from <see cref="Infrastructure.ICouchbaseDbContextOptionsBuilder"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class CouchbaseKeyspaceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified collection name.
    /// The scope defaults to "_default".
    /// </summary>
    /// <param name="collection">The collection name.</param>
    public CouchbaseKeyspaceAttribute(string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(collection);
        Collection = collection;
    }

    /// <summary>
    /// Initializes a new instance with the specified scope and collection names.
    /// </summary>
    /// <param name="scope">The scope name.</param>
    /// <param name="collection">The collection name.</param>
    public CouchbaseKeyspaceAttribute(string scope, string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(collection);
        Scope = scope;
        Collection = collection;
    }

    /// <summary>
    /// Gets the scope name. Defaults to "_default".
    /// </summary>
    public string Scope { get; } = "_default";

    /// <summary>
    /// Gets the collection name.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets the collection name (without bucket, which is added at runtime).
    /// </summary>
    /// <remarks>
    /// The full keyspace is constructed by <see cref="Extensions.CouchbaseModelBuilderExtensions.ConfigureToCouchbase"/>
    /// which adds the bucket and scope from the DbContext configuration.
    /// </remarks>
    public string GetKeySpace() => Collection;
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
