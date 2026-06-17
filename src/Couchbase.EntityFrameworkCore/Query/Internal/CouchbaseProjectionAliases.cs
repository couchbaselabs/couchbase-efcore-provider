using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Helpers for computing the result-key (alias) names used by N1QL projections.
/// <para>
/// Unlike a relational tabular result set — where columns are addressed positionally and
/// duplicate column names are harmless — a N1QL query returns each row as a JSON object keyed
/// by the projection alias.  When two projected columns share the same effective alias (for
/// example a collection <c>Include</c> where the principal and dependent both expose a
/// <c>rating</c> / <c>blogId</c> property) their values would collide on a single JSON key,
/// and <see cref="Storage.Internal.CouchbaseDbDataReader{T}"/> — which maps each shaper ordinal
/// to its alias — would read the same value into both ordinals.
/// </para>
/// <para>
/// To keep a one-to-one mapping between shaper ordinals and JSON keys, the alias of each
/// projection is made unique by appending an incrementing numeric suffix on collision.  The
/// SQL generator and the shaped-query compiler both derive their aliases from this method over
/// the same <see cref="SelectExpression.Projection"/> list (in the same order), so the emitted
/// <c>AS</c> clauses and the alias array handed to the reader stay aligned.
/// </para>
/// </summary>
internal static class CouchbaseProjectionAliases
{
    /// <summary>
    /// The N1QL response key for a single projection when no uniquification is applied:
    /// the explicit <c>AS</c> alias if present, otherwise the underlying column name.
    /// </summary>
    public static string EffectiveAlias(ProjectionExpression projection)
        => projection.Alias != string.Empty
            ? projection.Alias
            : projection.Expression is ColumnExpression c
                ? c.Name
                : string.Empty;

    /// <summary>
    /// Computes a collision-free alias for every projection, in projection order.  The first
    /// occurrence of an effective alias is kept verbatim; subsequent duplicates get an
    /// incrementing numeric suffix (e.g. <c>rating</c>, <c>rating0</c>, <c>rating1</c>).
    /// </summary>
    public static string[] ComputeUnique(IReadOnlyList<ProjectionExpression> projections)
    {
        var result = new string[projections.Count];
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < projections.Count; i++)
        {
            var name = EffectiveAlias(projections[i]);
            if (seen.TryGetValue(name, out var count))
            {
                // Find the next free suffixed name (guard against a literal collision with
                // an already-used "<name><n>").
                string candidate;
                do
                {
                    candidate = name + count;
                    count++;
                } while (seen.ContainsKey(candidate));

                seen[name] = count;
                seen[candidate] = 0;
                result[i] = candidate;
            }
            else
            {
                seen[name] = 0;
                result[i] = name;
            }
        }

        return result;
    }
}
