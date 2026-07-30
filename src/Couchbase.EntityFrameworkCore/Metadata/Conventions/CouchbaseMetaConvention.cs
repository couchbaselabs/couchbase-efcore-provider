using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// A convention that processes <see cref="CouchbaseMetaAttribute"/> on properties and configures
/// them to source their value from N1QL's <c>META()</c> function instead of a document field.
/// </summary>
public class CouchbaseMetaConvention : PropertyAttributeConventionBase<CouchbaseMetaAttribute>
{
    public CouchbaseMetaConvention(ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        CouchbaseMetaAttribute attribute,
        MemberInfo clrMember,
        IConventionContext context)
    {
        var expectedClrType = CouchbaseMetaFieldClrTypes.Get(attribute.Field);
        var clrType = propertyBuilder.Metadata.ClrType;
        if (clrType != expectedClrType)
        {
            throw new InvalidOperationException(
                $"[CouchbaseMeta({attribute.Field})] can only be applied to properties of type " +
                $"'{CouchbaseMetaFieldClrTypes.GetDisplayName(attribute.Field)}', but property " +
                $"'{propertyBuilder.Metadata.DeclaringType.ClrType.Name}.{propertyBuilder.Metadata.Name}' " +
                $"is of type '{clrType.Name}'.");
        }

        propertyBuilder.HasAnnotation(CouchbaseMetaAnnotationNames.MetaField, attribute.Field.ToString());
        propertyBuilder.ValueGenerated(ValueGenerated.OnAddOrUpdate);
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
