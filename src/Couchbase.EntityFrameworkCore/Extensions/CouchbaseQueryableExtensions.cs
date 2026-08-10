using System.Linq.Expressions;
using System.Reflection;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Couchbase.EntityFrameworkCore.Extensions;

/// <summary>
/// The N1QL index type a <see cref="CouchbaseQueryableExtensions.UseIndex{TEntity}"/> hint targets.
/// </summary>
public enum CouchbaseIndexType
{
    /// <summary>A Global Secondary Index (N1QL's <c>USING GSI</c>) — the default.</summary>
    Gsi,

    /// <summary>A Full Text Search index (N1QL's <c>USING FTS</c>).</summary>
    Fts,
}

/// <summary>
/// Which side of a hash join a <see cref="CouchbaseQueryableExtensions.UseHash{TEntity}"/> hint
/// builds vs. probes.
/// </summary>
public enum CouchbaseHashHintType
{
    /// <summary>This side builds the in-memory hash table (N1QL's <c>USE HASH(BUILD)</c>).</summary>
    Build,

    /// <summary>This side probes the hash table built by the other side (N1QL's <c>USE HASH(PROBE)</c>).</summary>
    Probe,
}

/// <summary>
/// Per-query N1QL optimizer hints — <c>USE INDEX</c> and <c>USE HASH</c> — attached to a specific
/// keyspace reference in a LINQ query, mirroring N1QL's own per-FROM-term placement
/// (<see href="https://docs.couchbase.com/server/current/n1ql/n1ql-language-reference/from.html"/>,
/// <see href="https://docs.couchbase.com/server/current/n1ql/n1ql-language-reference/hints.html"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EntityQueryProvider"/> is EF Core's single, shared query-provider implementation used
/// by every relational and non-relational EF Core provider alike (SQL Server, SQLite, the in-memory
/// provider, Couchbase, ...) — it is not a Couchbase-specific marker, so the <c>source.Provider is
/// EntityQueryProvider</c> check below only distinguishes "this is an EF Core queryable that will go
/// through query translation" from "this is a genuine non-EF-Core queryable" (e.g. LINQ-to-Objects'
/// own provider, produced by <c>List&lt;T&gt;.AsQueryable()</c>) — the latter has no translation
/// pipeline for either method to hook into at all, so it's a silent, benign no-op there: it returns
/// <paramref name="source"/> unchanged, since these are optimizer nudges, not correctness
/// requirements, and a query behaves identically whether or not a hint is honored.
/// </para>
/// <para>
/// Calling either method against an EF Core queryable backed by a provider OTHER than Couchbase
/// (SQL Server, SQLite, in-memory, ...) is <em>not</em> a no-op: the call is embedded in the query's
/// expression tree same as for Couchbase, but that other provider's translation pipeline has no
/// registered handling for <see cref="CouchbaseQueryableExtensions"/>'s methods, so it throws EF
/// Core's standard "translation failed" <see cref="InvalidOperationException"/> once the query is
/// executed — the same behavior any other provider-specific query extension exhibits when used
/// against the wrong provider (e.g. a SQL-Server-only <c>EF.Functions</c> method used with SQLite).
/// </para>
/// </remarks>
public static class CouchbaseQueryableExtensions
{
    /// <summary>
    /// Forces the query planner to use the named secondary index for this keyspace reference,
    /// instead of letting it choose — N1QL's <c>USE INDEX(name USING GSI|FTS)</c>. Only valid on
    /// the primary (root) keyspace of a query, not on the right-hand side of a join — use
    /// <see cref="UseHash{TEntity}"/> for join hints.
    /// </summary>
    /// <param name="source">The root queryable for the entity whose keyspace reference is hinted.</param>
    /// <param name="indexName">
    /// The index name, or <see langword="null"/> to broaden the hint to "any index of the given
    /// <paramref name="type"/>" (N1QL's own <c>USE INDEX(USING GSI)</c> form with no name).
    /// </param>
    /// <param name="type">The index type — <see cref="CouchbaseIndexType.Gsi"/> by default.</param>
    public static IQueryable<TEntity> UseIndex<TEntity>(
        this IQueryable<TEntity> source,
        [NotParameterized] string? indexName,
        [NotParameterized] CouchbaseIndexType type = CouchbaseIndexType.Gsi)
        where TEntity : class
        => source.Provider is EntityQueryProvider
            ? source.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    null,
                    UseIndexMethodInfo.MakeGenericMethod(typeof(TEntity)),
                    source.Expression,
                    Expression.Constant(indexName, typeof(string)),
                    Expression.Constant(type)))
            : source;

    /// <summary>
    /// Forces a hash-join strategy for this keyspace reference as the right-hand side of a join,
    /// instead of the default nested-loop join, and picks which side builds the hash table vs.
    /// probes it — N1QL's <c>USE HASH(BUILD)</c>/<c>USE HASH(PROBE)</c>. Apply this to the inner
    /// (right-hand) sequence passed to <c>Join</c>/<c>GroupJoin</c>, before the join itself:
    /// <c>outer.Join(inner.UseHash(CouchbaseHashHintType.Build), ...)</c>.
    /// </summary>
    /// <param name="source">The inner (right-hand) queryable of the join being hinted.</param>
    /// <param name="type">Which side of the hash join this sequence plays.</param>
    public static IQueryable<TEntity> UseHash<TEntity>(
        this IQueryable<TEntity> source, [NotParameterized] CouchbaseHashHintType type)
        where TEntity : class
        => source.Provider is EntityQueryProvider
            ? source.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    null,
                    UseHashMethodInfo.MakeGenericMethod(typeof(TEntity)),
                    source.Expression,
                    Expression.Constant(type)))
            : source;

    /// <summary>
    /// Constrains this query to a snapshot at least as recent as every write recorded in
    /// <paramref name="mutationState"/> — N1QL's <c>AtPlus</c> scan consistency, driven by a
    /// <see cref="MutationState"/> built from prior writes' <c>MutationToken</c>s (see
    /// <see cref="Couchbase.EntityFrameworkCore.Extensions.CouchbaseDatabaseFacadeExtensions.GetMutationState"/>
    /// for the common case of "see my own recent writes through this <c>DbContext</c>" --
    /// read-your-own-writes / RYOW). A <see langword="null"/> <paramref name="mutationState"/>
    /// leaves the query's consistency at whatever the context-wide
    /// <see cref="Couchbase.EntityFrameworkCore.Infrastructure.CouchbaseDbContextOptionsBuilder.ScanConsistency"/>
    /// setting already provides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="UseIndex{TEntity}"/>/<see cref="UseHash{TEntity}"/>, <paramref name="mutationState"/>
    /// is deliberately NOT marked <see cref="NotParameterizedAttribute"/> -- it must flow through EF
    /// Core's normal parameter extraction into a genuine query parameter, refreshed on every
    /// execution of a cached compiled query, rather than being baked into the query's cached SQL
    /// plan the way a <c>UseIndex</c>/<c>UseHash</c> hint safely can be. A <see cref="MutationState"/>
    /// is inherently per-call and would silently go stale if treated as a plan-level constant.
    /// </para>
    /// <para>
    /// Apply this as the LAST operator before enumeration (immediately before
    /// <c>ToListAsync()</c>/<c>FirstOrDefaultAsync()</c>/etc.), after any <c>.Where()</c>/<c>.OrderBy()</c>/
    /// <c>.Select()</c>. This is enforced by construction, not just convention: internally, this
    /// method wraps the query's shaper (materialization) expression in a marker that carries the
    /// <paramref name="mutationState"/> parameter's name forward to execution time, and composing
    /// ANY further operator afterward that needs to re-inspect that shaper's shape --
    /// <c>.Where()</c>, <c>.OrderBy()</c>, <c>.Select()</c>, or anything else -- fails with EF
    /// Core's standard "could not be translated" <see cref="InvalidOperationException"/> (confirmed
    /// by a regression test), rather than silently ignoring the hint or misapplying it. This is a
    /// deliberately loud failure mode for what is a correctness-affecting option, not merely an
    /// optimizer nudge the way <c>UseIndex</c>/<c>UseHash</c> are.
    /// </para>
    /// </remarks>
    /// <param name="source">The query to constrain.</param>
    /// <param name="mutationState">
    /// The mutations this query must be consistent with, or <see langword="null"/> for no
    /// per-query constraint.
    /// </param>
    public static IQueryable<TEntity> ConsistentWith<TEntity>(
        this IQueryable<TEntity> source, MutationState? mutationState)
        where TEntity : class
        => source.Provider is EntityQueryProvider
            ? source.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    null,
                    ConsistentWithMethodInfo.MakeGenericMethod(typeof(TEntity)),
                    source.Expression,
                    Expression.Constant(mutationState, typeof(MutationState))))
            : source;

    internal static readonly MethodInfo UseIndexMethodInfo
        = typeof(CouchbaseQueryableExtensions).GetMethod(nameof(UseIndex))!;

    internal static readonly MethodInfo UseHashMethodInfo
        = typeof(CouchbaseQueryableExtensions).GetMethod(nameof(UseHash))!;

    internal static readonly MethodInfo ConsistentWithMethodInfo
        = typeof(CouchbaseQueryableExtensions).GetMethod(nameof(ConsistentWith))!;
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
