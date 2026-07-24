using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

public class CouchbaseGuidMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo NewGuidMethodInfo
        = typeof(Guid).GetRuntimeMethod(nameof(Guid.NewGuid), Type.EmptyTypes)!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public CouchbaseGuidMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (NewGuidMethodInfo.Equals(method))
        {
            var function = _sqlExpressionFactory.Function(
                "UUID",
                Array.Empty<SqlExpression>(),
                nullable: false,
                argumentsPropagateNullability: Array.Empty<bool>(),
                method.ReturnType);

            // Without an explicit type mapping the materializer can't determine how to read the
            // result correctly in a top-level scalar projection (confirmed against a live
            // cluster: "Nullable object must have a value" from the generated shaper).
            return _sqlExpressionFactory.ApplyDefaultTypeMapping(function);
        }

        return null;
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
