using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json.Nodes;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Provides type mappings between .NET CLR types and Couchbase JSON types.
/// </summary>
/// <remarks>
/// Couchbase stores documents as JSON, which supports the following types:
/// <list type="bullet">
/// <item><description>NUMBER - All numeric types (integers, decimals, floats)</description></item>
/// <item><description>STRING - Text, dates (ISO 8601), GUIDs, Base64 binary</description></item>
/// <item><description>BOOLEAN - true/false</description></item>
/// <item><description>OBJECT - Nested documents</description></item>
/// <item><description>ARRAY - Lists and arrays</description></item>
/// <item><description>NULL - null values</description></item>
/// </list>
/// </remarks>
public class CouchbaseTypeMappingSource : RelationalTypeMappingSource
{
    private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;

    public CouchbaseTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
        _clrTypeMappings = new Dictionary<Type, RelationalTypeMapping>
        {
            // Numeric types -> NUMBER
            { typeof(int), new IntTypeMapping("NUMBER") },
            { typeof(uint), new UIntTypeMapping("NUMBER") },
            { typeof(long), new LongTypeMapping("NUMBER") },
            { typeof(ulong), new ULongTypeMapping("NUMBER") },
            { typeof(short), new ShortTypeMapping("NUMBER") },
            { typeof(ushort), new UShortTypeMapping("NUMBER") },
            { typeof(byte), new ByteTypeMapping("NUMBER") },
            { typeof(sbyte), new SByteTypeMapping("NUMBER") },
            { typeof(float), new FloatTypeMapping("NUMBER") },
            { typeof(double), new DoubleTypeMapping("NUMBER") },
            { typeof(decimal), new DecimalTypeMapping("NUMBER") },

            // Boolean -> BOOLEAN
            { typeof(bool), new BoolTypeMapping("BOOLEAN") },

            // String types -> STRING
            { typeof(string), new StringTypeMapping("STRING", DbType.String) },
            { typeof(char), new CharTypeMapping("STRING", DbType.StringFixedLength) },

            // Date/time types -> STRING (ISO 8601 format)
            { typeof(DateTime), new DateTimeTypeMapping("STRING", DbType.DateTime) },
            { typeof(DateTimeOffset), new DateTimeOffsetTypeMapping("STRING", DbType.DateTimeOffset) },
            { typeof(DateOnly), new DateOnlyTypeMapping("STRING") },
            { typeof(TimeOnly), new TimeOnlyTypeMapping("STRING") },
            { typeof(TimeSpan), new TimeSpanTypeMapping("STRING") },

            // Other types -> STRING
            { typeof(Guid), new GuidTypeMapping("STRING", DbType.Guid) },
            { typeof(byte[]), new ByteArrayTypeMapping("STRING", DbType.Binary) },

            // JSON types
            {
                typeof(JsonObject),
                new CouchbaseTypeMapping(
                    typeof(JsonObject),
                    "OBJECT",
                    jsonValueReaderWriter: dependencies.JsonValueReaderWriterSource.FindReaderWriter(typeof(JsonObject)))
            },
            {
                typeof(JsonArray),
                new CouchbaseTypeMapping(
                    typeof(JsonArray),
                    "ARRAY",
                    jsonValueReaderWriter: dependencies.JsonValueReaderWriterSource.FindReaderWriter(typeof(JsonArray)))
            }
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
