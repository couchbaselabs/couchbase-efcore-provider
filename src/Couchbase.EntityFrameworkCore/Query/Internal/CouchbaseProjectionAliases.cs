using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata;
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
    /// A stable, collision-free lookup key for an owned navigation, used to correlate a
    /// navigation with its actual (possibly uniquified) N1QL result-row key — see
    /// <see cref="Query.Internal.CouchbaseShapedQueryCompilingExpressionVisitor.AddOwnedNavigationColumnsToProjection"/>
    /// and <see cref="Query.Internal.CouchbaseOwnedCollectionMaterializer"/>. Qualified by the
    /// declaring entity type so two sibling TPH-derived types that happen to declare an owned
    /// navigation with the same name (e.g. both a <c>Student</c> and a <c>Teacher</c> owning a
    /// <c>Documents</c> collection) don't collide on this key even though their raw
    /// <see cref="INavigation.Name"/> values are identical.
    /// </summary>
    public static string NavigationKey(INavigation navigation)
        => navigation.DeclaringEntityType.ClrType.FullName + "." + navigation.Name;

    /// <summary>
    /// The JSON field name an owned-collection navigation is stored under, given the configured
    /// <c>FieldNamingPolicy</c> — the raw CLR navigation name if no policy is configured (or the
    /// policy leaves it unchanged), otherwise the policy-converted name (e.g. <c>"contactMethods"</c>
    /// for a <c>ContactMethods</c> navigation under the default camelCase policy). Shared between
    /// <see cref="Query.Internal.CouchbaseShapedQueryCompilingExpressionVisitor.AddOwnedNavigationColumnsToProjection"/>
    /// (read-path projection) and <see cref="CouchbaseQuerySqlGenerator"/>'s <c>ANY...SATISFIES</c>
    /// rendering for <c>.Any(predicate)</c> over an owned collection, so the two can never drift.
    /// </summary>
    public static string GetOwnedCollectionFieldName(INavigation navigation, JsonNamingPolicy? fieldNamingPolicy)
        => fieldNamingPolicy?.ConvertName(navigation.Name) ?? navigation.Name;

    /// <summary>
    /// The <see cref="CouchbaseMetaField"/> name (e.g. <c>"Cas"</c>) a column is sourced from via
    /// <c>[CouchbaseMeta]</c>/<c>HasCouchbaseMeta</c>, or <see langword="null"/> if it's a normal
    /// document field. <see cref="ColumnExpression.Column"/> exposes a live <see cref="IProperty"/>
    /// reference (via <c>PropertyMappings</c>) back to the property that produced this column --
    /// including shadow properties -- so this works without any change to how EF Core builds the
    /// projection itself. Shared between <see cref="EffectiveAlias"/> (so the alias-uniquification
    /// pass knows a META column's *actual* N1QL-implicit name is the lowercase field name, not the
    /// property's own name) and <see cref="CouchbaseQuerySqlGenerator"/>'s rendering of the column
    /// itself.
    /// </summary>
    public static string? GetMetaField(ColumnExpression columnExpression)
        => columnExpression.Column?.PropertyMappings
            .Select(m => m.Property.FindAnnotation(CouchbaseMetaAnnotationNames.MetaField)?.Value as string)
            .FirstOrDefault(f => f != null);

    /// <summary>
    /// The N1QL response key for a single projection when no uniquification is applied:
    /// the explicit <c>AS</c> alias if present, otherwise the underlying column name. This is the
    /// *desired* key (what the reader expects), which for a <c>META(alias).field</c> projection is
    /// still the property's own name (e.g. <c>"DocId"</c>) -- see <see cref="NeedsExplicitAlias"/>
    /// for why such a projection can never rely on N1QL's own implicit naming to produce that key.
    /// </summary>
    public static string EffectiveAlias(ProjectionExpression projection)
        => projection.Alias != string.Empty
            ? projection.Alias
            : projection.Expression is ColumnExpression c
                ? c.Name
                : string.Empty;

    /// <summary>
    /// Whether a projection must get an explicit <c>AS &lt;alias&gt;</c> regardless of what the
    /// collision-avoidance pass in <see cref="ComputeUnique"/> decided -- true for any
    /// <c>META(alias).field</c> projection, since N1QL's own implicit-naming behavior for a
    /// function-call-based expression doesn't reliably (or ever, empirically) produce the
    /// property's own name (e.g. <c>META(d).id</c> does not implicitly come back keyed
    /// <c>"DocId"</c>, or even reliably <c>"id"</c>) the way a bare <c>alias.field</c> reference's
    /// implicit name always matches its own column name.
    /// </summary>
    public static bool NeedsExplicitAlias(ProjectionExpression projection)
        => projection.Expression is ColumnExpression c && GetMetaField(c) != null;

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
