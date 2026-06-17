using System;
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
        var names = new string[projections.Count];
        for (var i = 0; i < projections.Count; i++)
            names[i] = EffectiveAlias(projections[i]);
        return MakeUnique(names);
    }

    /// <summary>
    /// Makes a list of alias names collision-free, preserving order: the first occurrence of
    /// each name is kept verbatim and later duplicates receive the smallest numeric suffix that
    /// is neither already emitted nor an original (reserved) literal.  Reserving every input name
    /// up front ensures a generated suffix never steals a distinct literal alias that appears
    /// later in the list (e.g. <c>["rating", "rating", "rating0"]</c> →
    /// <c>["rating", "rating1", "rating0"]</c>, not <c>["rating", "rating0", "rating00"]</c>).
    /// <para>
    /// Collisions are detected case-insensitively to match
    /// <see cref="Storage.Internal.CouchbaseDbDataReader{T}"/>, which keys its alias→ordinal map
    /// with <see cref="StringComparer.OrdinalIgnoreCase"/>.  Aliases differing only by case
    /// (e.g. <c>rating</c> / <c>Rating</c>) would otherwise collide at read time even though this
    /// method left them untouched.  The base name's original casing is preserved in the suffixed
    /// result (<c>Rating</c> → <c>Rating0</c>).
    /// </para>
    /// </summary>
    public static string[] MakeUnique(IReadOnlyList<string> names)
    {
        var result = new string[names.Count];
        // Case-insensitive to match CouchbaseDbDataReader's alias lookup.
        var reserved = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);  // every original literal alias
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);             // names already emitted into result
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (used.Add(name))
            {
                // First time this exact literal is emitted — always keep it verbatim.
                result[i] = name;
                continue;
            }

            // Duplicate: pick the smallest "<name><n>" that is not already emitted and is not an
            // original literal (so a later distinct literal keeps its own slot).
            var n = 0;
            string candidate;
            do
            {
                candidate = name + n++;
            } while (used.Contains(candidate) || reserved.Contains(candidate));

            used.Add(candidate);
            result[i] = candidate;
        }

        return result;
    }
}
