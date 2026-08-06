using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Extensions;

/// <summary>
/// Couchbase-specific functions usable inside a LINQ query via <see cref="EF.Functions"/>, mirroring
/// how EF Core's own <c>DbFunctions</c>-based methods (e.g. <c>Like</c>, <c>Greatest</c>) expose
/// provider/relational-specific SQL through the same marker-object pattern.
/// </summary>
/// <remarks>
/// N1QL documents can have a field that is genuinely <c>MISSING</c> (absent from the JSON
/// entirely), which is a distinct concept from the field being present with a JSON <c>null</c>
/// value — something ordinary <c>== null</c>/<c>.HasValue</c> LINQ cannot express, since EF Core's
/// own null-semantics treat a missing column read as a CLR default/null indistinguishably from an
/// actual stored null. These four methods expose N1QL's own <c>IS [NOT] MISSING</c>/
/// <c>IS [NOT] VALUED</c> postfix operators to make that distinction queryable.
/// </remarks>
public static class CouchbaseDbFunctionsExtensions
{
    /// <summary>
    /// Translates to N1QL's <c>IS MISSING</c>: <see langword="true"/> when the JSON field backing
    /// <paramref name="value"/> is absent from the document entirely, as distinct from being
    /// present with a JSON <c>null</c> (which ordinary <c>value == null</c> already covers).
    /// </summary>
    /// <remarks>
    /// This method has no client-side (in-memory) implementation — it can only be used inside a
    /// LINQ query that gets translated to SQL++; calling it directly throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public static bool IsMissing<T>(this DbFunctions _, T value)
        => throw new InvalidOperationException(ClientEvalMessage(nameof(IsMissing)));

    /// <summary>
    /// Translates to N1QL's <c>IS NOT MISSING</c>: <see langword="true"/> when the JSON field
    /// backing <paramref name="value"/> is present in the document, whether its value is JSON
    /// <c>null</c> or a real value.
    /// </summary>
    /// <remarks>
    /// This method has no client-side (in-memory) implementation — it can only be used inside a
    /// LINQ query that gets translated to SQL++; calling it directly throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public static bool IsNotMissing<T>(this DbFunctions _, T value)
        => throw new InvalidOperationException(ClientEvalMessage(nameof(IsNotMissing)));

    /// <summary>
    /// Translates to N1QL's <c>IS VALUED</c>: <see langword="true"/> when the JSON field backing
    /// <paramref name="value"/> is present in the document AND is not JSON <c>null</c> — i.e. it
    /// holds a real value.
    /// </summary>
    /// <remarks>
    /// This method has no client-side (in-memory) implementation — it can only be used inside a
    /// LINQ query that gets translated to SQL++; calling it directly throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public static bool IsValued<T>(this DbFunctions _, T value)
        => throw new InvalidOperationException(ClientEvalMessage(nameof(IsValued)));

    /// <summary>
    /// Translates to N1QL's <c>IS NOT VALUED</c>: <see langword="true"/> when the JSON field
    /// backing <paramref name="value"/> is either absent from the document entirely or present
    /// with a JSON <c>null</c> value.
    /// </summary>
    /// <remarks>
    /// This method has no client-side (in-memory) implementation — it can only be used inside a
    /// LINQ query that gets translated to SQL++; calling it directly throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public static bool IsNotValued<T>(this DbFunctions _, T value)
        => throw new InvalidOperationException(ClientEvalMessage(nameof(IsNotValued)));

    private static string ClientEvalMessage(string methodName)
        => $"'{methodName}' can only be translated to SQL++ within a LINQ query against a Couchbase " +
           "collection; it has no client-side (in-memory) implementation.";
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
