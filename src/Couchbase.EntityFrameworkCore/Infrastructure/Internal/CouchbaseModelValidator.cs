using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseModelValidator : RelationalModelValidator
{
    public CouchbaseModelValidator(ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies) : base(dependencies, relationalDependencies)
    {
        
    }
    
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);
        ValidateSequenceTypes(model);
    }

    /// <summary>
    /// A TPC hierarchy stores each concrete type in its own collection, so a generated key must be
    /// unique across them. EF Core warns about any generated TPC key, because an identity column
    /// generates values per table. A key drawn from a Couchbase sequence is shared by the whole
    /// hierarchy, so it is accepted; any other generated TPC key still gets the warning.
    /// </summary>
    protected override void ValidateValueGeneration(
        IEntityType entityType, IKey key, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        if (key.Properties.All(p => p.ValueGenerated == ValueGenerated.Never
                || p.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation) != null))
        {
            return;
        }

        base.ValidateValueGeneration(entityType, key, logger);
    }

    /// <summary>
    /// A Couchbase sequence produces integers, so it can only feed an integer (or decimal) property.
    /// </summary>
    private static void ValidateSequenceTypes(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation) is null)
                {
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!CouchbaseSequenceTypes.IsSupported(type))
                {
                    throw new InvalidOperationException(
                        $"Cannot use Couchbase sequence for property '{entityType.DisplayName()}.{property.Name}' "
                        + $"because the property type '{property.ClrType.Name}' is not supported. "
                        + "Supported types are: int, long, short, byte, uint, ulong, ushort, decimal.");
                }
            }
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
