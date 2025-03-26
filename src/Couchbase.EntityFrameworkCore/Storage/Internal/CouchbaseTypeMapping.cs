using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseTypeMapping : RelationalTypeMapping
{
    private readonly Type _clrType;
    private readonly ValueComparer? _comparer;
    private readonly ValueComparer? _keyComparer;
    private readonly JsonValueReaderWriter? _jsonValueReaderWriter;

    protected CouchbaseTypeMapping(RelationalTypeMappingParameters relationalParameters) : base(relationalParameters)
    {
    }
    
   public CouchbaseTypeMapping(
        Type clrType,
        ValueComparer? comparer = null,
        ValueComparer? keyComparer = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null) 
       : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(clrType), "couchbase"))
   {
       _clrType = clrType;
       _comparer = comparer;
       _keyComparer = keyComparer;
       _jsonValueReaderWriter = jsonValueReaderWriter;
   }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new CouchbaseTypeMapping(parameters);
    }

    public override CoreTypeMapping WithComposedConverter(ValueConverter? converter, ValueComparer? comparer = null,
        ValueComparer? keyComparer = null, CoreTypeMapping? elementMapping = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null)
    {
        return new CouchbaseTypeMapping(Parameters.WithComposedConverter(converter, comparer, keyComparer,
            elementMapping, jsonValueReaderWriter));
    }

    /// <summary>
    /// SQL++ requires that that discriminator values are encased in quotation marks. This may break other things...
    /// </summary>
    protected override string SqlLiteralFormatString => "\"{0}\"";
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
