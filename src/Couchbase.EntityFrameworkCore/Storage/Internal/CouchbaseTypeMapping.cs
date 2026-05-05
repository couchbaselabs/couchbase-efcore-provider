using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Type mapping for Couchbase JSON types (OBJECT and ARRAY).
/// </summary>
public class CouchbaseTypeMapping : RelationalTypeMapping
{
    /// <summary>
    /// Creates a new instance from existing parameters (used for cloning).
    /// </summary>
    protected CouchbaseTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>
    /// Creates a new Couchbase type mapping.
    /// </summary>
    /// <param name="clrType">The CLR type being mapped.</param>
    /// <param name="storeType">The Couchbase store type (e.g., "OBJECT", "ARRAY").</param>
    /// <param name="comparer">Optional value comparer.</param>
    /// <param name="keyComparer">Optional key comparer.</param>
    /// <param name="jsonValueReaderWriter">Optional JSON reader/writer.</param>
    public CouchbaseTypeMapping(
        Type clrType,
        string storeType,
        ValueComparer? comparer = null,
        ValueComparer? keyComparer = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(clrType)
                .WithComposedConverter(null, comparer, keyComparer, null, jsonValueReaderWriter),
            storeType))
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new CouchbaseTypeMapping(parameters);
    }

    /// <inheritdoc />
    public override CoreTypeMapping WithComposedConverter(
        ValueConverter? converter,
        ValueComparer? comparer = null,
        ValueComparer? keyComparer = null,
        CoreTypeMapping? elementMapping = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null)
    {
        return new CouchbaseTypeMapping(
            Parameters.WithComposedConverter(converter, comparer, keyComparer, elementMapping, jsonValueReaderWriter));
    }

    /// <summary>
    /// SQL++ requires string literals to be enclosed in double quotes.
    /// </summary>
    protected override string SqlLiteralFormatString => "\"{0}\"";
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
