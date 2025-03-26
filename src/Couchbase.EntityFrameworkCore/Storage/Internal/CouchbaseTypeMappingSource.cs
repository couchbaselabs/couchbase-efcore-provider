using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json.Nodes;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseTypeMappingSource: RelationalTypeMappingSource
{
    private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;
    
    public CouchbaseTypeMappingSource(TypeMappingSourceDependencies dependencies, RelationalTypeMappingSourceDependencies relationalDependencies) 
        : base(dependencies, relationalDependencies)
    {
        _clrTypeMappings
            = new Dictionary<Type, RelationalTypeMapping>
            {
                {
                    typeof(JsonObject), new CouchbaseTypeMapping(
                        typeof(JsonObject), jsonValueReaderWriter: dependencies.JsonValueReaderWriterSource.FindReaderWriter(typeof(JsonObject)))
                },
                {
                    typeof(string), new CouchbaseTypeMapping(
                        typeof(string), jsonValueReaderWriter: dependencies.JsonValueReaderWriterSource.FindReaderWriter(typeof(string)))
                },
                {
                    typeof(int), new IntTypeMapping("NUMBER")
                },
                {
                    typeof(uint), new UIntTypeMapping("NUMBER")
                },
                {
                    typeof(double), new DoubleTypeMapping("NUMBER")
                },
                {
                    typeof(long), new LongTypeMapping("NUMBER")
                },
                {
                    typeof(bool), new BoolTypeMapping("BOOLEAN")
                }
                /*,
                {
                    typeof(string), new StringTypeMapping("STRING", DbType.String)
                }*/
                //TODO add the rest of the type mappings
            };
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
       var clrType = mappingInfo.ClrType;
       if (clrType == null)
       {
           throw new InvalidOperationException($"Cannot map type {clrType}");
       }

       return _clrTypeMappings.TryGetValue(clrType, out var mapping) ? mapping : base.FindMapping(in mappingInfo);
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
