using System.Reflection;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Couchbase.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// A convention that processes <see cref="DateTimeFormatAttribute"/> on properties and configures
/// them to use a per-property <see cref="DateTime"/> string format override.
/// </summary>
public class DateTimeFormatConvention : PropertyAttributeConventionBase<DateTimeFormatAttribute>
{
    public DateTimeFormatConvention(ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        DateTimeFormatAttribute attribute,
        MemberInfo clrMember,
        IConventionContext context)
    {
        // CouchbaseTypeMappingSource only honors this annotation for DateTime/DateTime? --
        // misapplying the attribute to any other property type would otherwise be a silent no-op.
        var clrType = propertyBuilder.Metadata.ClrType;
        if (clrType != typeof(DateTime) && clrType != typeof(DateTime?))
        {
            throw new InvalidOperationException(
                $"[DateTimeFormat] can only be applied to properties of type DateTime or DateTime?, but property " +
                $"'{propertyBuilder.Metadata.DeclaringType.ClrType.Name}.{propertyBuilder.Metadata.Name}' " +
                $"is of type '{clrType.Name}'.");
        }

        propertyBuilder.HasAnnotation(CouchbaseTypeMappingSource.DateTimeFormatAnnotation, attribute.Format);
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
