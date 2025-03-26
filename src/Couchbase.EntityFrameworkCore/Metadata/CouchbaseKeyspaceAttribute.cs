using System.Text;
using Couchbase.Core.Utils;

namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Maps an entity to a Scope and Collection. The other part of the keyspace,
/// the Bucket name, is pulled from the ClusterOptions that is injected via DI
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CouchbaseKeyspaceAttribute : Attribute
{
    private string _keyspace;
    
    public CouchbaseKeyspaceAttribute(string? collection)
    {
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
    }
    
    public CouchbaseKeyspaceAttribute(string? scope, string? collection)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
    }
    
    public string? Scope { get; } = "_default";

    public string? Collection { get; } = "_default";

    public string GetKeySpace()
    {
        if (_keyspace == null)
        {
            var keyspaceBuilder = new StringBuilder();
            keyspaceBuilder.Append(Collection);
            _keyspace = keyspaceBuilder.ToString();
        }

        return _keyspace;
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
