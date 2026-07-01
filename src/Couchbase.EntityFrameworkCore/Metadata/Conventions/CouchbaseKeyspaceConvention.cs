using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

public class CouchbaseKeyspaceConvention : TypeAttributeConventionBase<CouchbaseKeyspaceAttribute>
{
    /// <summary>
    /// Annotation key used to store the scope override for an entity.
    /// </summary>
    public const string ScopeOverrideAnnotation = "Couchbase:ScopeOverride";

    public CouchbaseKeyspaceConvention(ProviderConventionSetBuilderDependencies dependencies) : base(dependencies)
    {
    }

    protected override void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        CouchbaseKeyspaceAttribute keyspaceAttribute,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        // When the attribute specifies an explicit bucket, store the full keyspace
        // (Bucket.Scope.Collection) directly as the table name. ConfigureToCouchbase then
        // leaves it untouched (it only fills in the bucket/scope for bare collection names),
        // so the entity is mapped to a bucket other than the DbContext's configured one.
        if (keyspaceAttribute.Bucket is not null)
        {
            var keyspace = new Metadata.CouchbaseKeyspace(
                keyspaceAttribute.Bucket, keyspaceAttribute.Scope!, keyspaceAttribute.Collection);
            entityTypeBuilder.ToTable(keyspace.ToString());
            return;
        }

        // Set the collection name as the table name
        entityTypeBuilder.ToTable(keyspaceAttribute.Collection);

        // If the attribute has a scope override, store it as an annotation
        if (keyspaceAttribute.HasScopeOverride)
        {
            entityTypeBuilder.HasAnnotation(ScopeOverrideAnnotation, keyspaceAttribute.Scope);
        }
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
