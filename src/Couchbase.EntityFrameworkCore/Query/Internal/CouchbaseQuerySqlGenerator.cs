using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Text;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGenerator : QuerySqlGenerator
{
    private readonly ConcurrentDictionary<string, CouchbaseKeyspace> _tableNameCache = new();
    private readonly System.Text.Json.JsonNamingPolicy? _fieldNamingPolicy;

    // Set only while rendering the SATISFIES clause of an owned-collection ANY(...) expression
    // (see GenerateExists/TryRenderOwnedCollectionAny) -- ColumnExpressions bound to this alias
    // refer to properties of an array element embedded in JSON, which are written under the
    // policy-converted name (e.g. "title", not "Title"), unlike every other column this generator
    // renders. Saved/restored around the Visit() call rather than cleared, since a query can
    // contain more than one such ANY(...) expression (e.g. `a.Xs.Any(...) || a.Ys.Any(...)`).
    private string? _ownedAnyAlias;

    public CouchbaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, System.Text.Json.JsonNamingPolicy? fieldNamingPolicy = null) : base(dependencies)
    {
        _fieldNamingPolicy = fieldNamingPolicy;
    }

    /// <inheritdoc />
    protected override Expression VisitCrossJoin(CrossJoinExpression crossJoinExpression)
    {
        return base.VisitCrossJoin(crossJoinExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitLeftJoin(LeftJoinExpression leftJoinExpression)
    {
        // Skip LEFT JOINs for owned types — they are embedded in their owner's document
        // and have no independent keyspace.
        // Direct-table form: LEFT JOIN ownedTable AS alias ON …
        if (leftJoinExpression.Table is TableExpression tableExpression && IsOwnedTable(tableExpression))
            return leftJoinExpression;

        // Lateral-subquery form: LEFT JOIN (SELECT … FROM ownedTable) AS alias ON …
        // EF Core emits this shape when OwnsMany items have nested owned navigations.
        if (leftJoinExpression.Table is SelectExpression innerSelect && IsAllOwnedTablesSelect(innerSelect))
            return leftJoinExpression;

        return base.VisitLeftJoin(leftJoinExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitInnerJoin(InnerJoinExpression innerJoinExpression)
    {
        // Skip INNER JOINs for owned types — they are embedded in their owner's document
        // and have no independent keyspace.
        // Direct-table form: INNER JOIN ownedTable AS alias ON …
        if (innerJoinExpression.Table is TableExpression tableExpression && IsOwnedTable(tableExpression))
            return innerJoinExpression;

        // Lateral-subquery form: INNER JOIN (SELECT … FROM ownedTable) AS alias ON …
        if (innerJoinExpression.Table is SelectExpression innerSelect && IsAllOwnedTablesSelect(innerSelect))
            return innerJoinExpression;

        return base.VisitInnerJoin(innerJoinExpression);
    }

    /// <summary>
    /// Returns <see langword="true"/> when every entity type mapped to
    /// <paramref name="tableExpression"/> is an owned type.  Such tables have no independent
    /// Couchbase keyspace — their data is embedded in the owner's document — and must be
    /// skipped when emitting FROM / JOIN clauses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check uses <c>All</c>, not <c>Any</c>.  An owner table that also hosts
    /// <c>OwnsOne</c> scalar navigations (table-splitting) will have both the owner entity
    /// type <em>and</em> the owned entity type in <see cref="ITableBase.EntityTypeMappings"/>.
    /// <c>Any</c> would incorrectly mark the owner's table as owned, causing its FROM clause
    /// to be suppressed and producing a N1QL syntax error (<c>WHERE</c> with no preceding
    /// <c>FROM</c>).  <c>All</c> only returns <see langword="true"/> for tables whose
    /// mappings are exclusively owned types — i.e., a separate OwnsMany-item table that has
    /// no corresponding Couchbase collection.
    /// </para>
    /// <para>
    /// The empty-collection guard prevents vacuous <c>All</c> from returning
    /// <see langword="true"/> for unmapped tables.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Emits the <c>ORDER BY</c> clause, dropping any ordering terms that reference a
    /// suppressed owned-join alias.  Without this filter the alias appears in
    /// <c>ORDER BY</c> but never in <c>FROM</c>/<c>JOIN</c>, producing malformed SQL
    /// that survives only because N1QL evaluates undefined identifiers as
    /// <c>MISSING</c> rather than raising an error.
    /// </summary>
    protected override void GenerateOrderings(SelectExpression selectExpression)
    {
        // Collect the aliases of every owned-type JOIN that VisitLeftJoin/VisitInnerJoin
        // will suppress so that their ORDER BY terms can be dropped symmetrically.
        var skippedAliases = CollectOwnedJoinAliases(selectExpression);
        if (skippedAliases.Count == 0)
        {
            base.GenerateOrderings(selectExpression);
            return;
        }

        // Filter out orderings whose sole column reference is to a suppressed alias.
        // Orderings on owner columns or literals are kept unchanged.
        var filtered = selectExpression.Orderings
            .Where(o => o.Expression is not ColumnExpression col
                        || !skippedAliases.Contains(col.TableAlias))
            .ToList();

        if (filtered.Count == 0) return;

        // EF Core 10 does not expose a per-ordering virtual hook (GenerateOrdering was
        // removed in modern versions; only GenerateOrderings(SelectExpression) exists).
        // Emit each surviving ordering via a private helper so the formatting is defined
        // in one place and stays in sync with the base class's simple ASC/DESC convention.
        Sql.AppendLine().Append("ORDER BY ");
        GenerateList(filtered, EmitOrdering);
    }

    /// <summary>
    /// Emits a single ordering term (<c>expression ASC|DESC</c>) into the SQL buffer.
    /// Extracted so <see cref="GenerateOrderings"/> does not inline formatting logic
    /// that would silently diverge if the base class ever adds NULLS FIRST/LAST or
    /// collation support.
    /// </summary>
    private void EmitOrdering(OrderingExpression ordering)
    {
        Visit(ordering.Expression);
        Sql.Append(ordering.IsAscending ? " ASC" : " DESC");
    }

    private static bool IsOwnedTable(TableExpression tableExpression)
    {
        // Single-pass enumeration: track whether any mappings exist and whether every
        // mapping seen so far is an owned entity type. Avoids the ToList() allocation
        // that would otherwise occur on every FROM/JOIN clause generation.
        var any = false;
        foreach (var mapping in tableExpression.Table.EntityTypeMappings)
        {
            if (mapping.TypeBase is not IEntityType et || !et.IsOwned())
                return false;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Same check as <see cref="IsOwnedTable"/>, but also returns every owned <see cref="IEntityType"/>
    /// mapped to the table — needed by <see cref="TryRenderOwnedCollectionAny"/> to resolve the
    /// owning navigation via <see cref="IEntityType.FindOwnership"/>.
    /// </summary>
    /// <remarks>
    /// A table can carry more than one owned <see cref="IEntityType"/> when the OwnsMany item
    /// type itself table-splits a nested <c>OwnsOne</c> (e.g. <c>ContactMethod</c> hosting its own
    /// <c>Label</c>) — both mappings pass <see cref="IsOwnedTable"/>'s <c>All</c> check, but only
    /// one of them (<c>ContactMethod</c>, not <c>Label</c>) is the collection's own item type whose
    /// ownership correlates to the correct parent. There's no cheap way to tell which from the
    /// table alone, so every candidate is returned and the caller tries each in turn.
    /// </remarks>
    private static bool TryGetOwnedEntityTypes(TableExpression tableExpression, out List<IEntityType> entityTypes)
    {
        entityTypes = new List<IEntityType>();
        foreach (var mapping in tableExpression.Table.EntityTypeMappings)
        {
            if (mapping.TypeBase is not IEntityType et || !et.IsOwned())
            {
                entityTypes = new List<IEntityType>();
                return false;
            }
            entityTypes.Add(et);
        }
        return entityTypes.Count > 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> when every table referenced by
    /// <paramref name="selectExpression"/> (directly and through its own JOINs) is an owned
    /// type.  Used to detect EF Core's lateral-join subquery pattern for OwnsMany items that
    /// have nested owned navigations — the whole subquery can be skipped because its data is
    /// embedded in the parent document.
    /// </summary>
    internal static bool IsAllOwnedTablesSelect(SelectExpression selectExpression)
    {
        if (selectExpression.Tables.Count == 0) return false;
        foreach (var table in selectExpression.Tables)
        {
            switch (table)
            {
                // Direct owned table — base case.
                case TableExpression te when IsOwnedTable(te):
                    break;

                // JOIN whose inner table is a direct owned table.
                case LeftJoinExpression  lj when lj.Table is TableExpression ljTe && IsOwnedTable(ljTe):
                    break;
                case InnerJoinExpression ij when ij.Table is TableExpression ijTe && IsOwnedTable(ijTe):
                    break;

                // JOIN whose inner table is itself a lateral subquery — recurse so that
                // depth ≥ 3 nested OwnsMany chains (e.g. Customer → Methods → Tags → Audits)
                // are correctly identified and their JOINs suppressed.
                case LeftJoinExpression  lj when lj.Table is SelectExpression ljInner && IsAllOwnedTablesSelect(ljInner):
                    break;
                case InnerJoinExpression ij when ij.Table is SelectExpression ijInner && IsAllOwnedTablesSelect(ijInner):
                    break;

                // Anything else (non-owned table, unrecognised shape) → not all owned.
                default:
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns the set of table aliases for owned-type LEFT JOIN / INNER JOIN entries in
    /// <paramref name="selectExpression"/> — those that <see cref="VisitLeftJoin"/> and
    /// <see cref="VisitInnerJoin"/> will suppress when generating SQL.
    /// Includes both direct-table joins (<c>LEFT JOIN owned AS alias</c>) and lateral-join
    /// subqueries (<c>LEFT JOIN (SELECT … FROM owned …) AS alias</c>).
    /// Used by <see cref="VisitSelect"/> to filter owned-join columns from the emitted
    /// SELECT projection so N1QL does not see references to undefined table aliases.
    /// </summary>
    internal static HashSet<string> CollectOwnedJoinAliases(SelectExpression selectExpression)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in selectExpression.Tables)
        {
            switch (table)
            {
                // Direct owned-table join: LEFT JOIN ownedTable AS alias
                case LeftJoinExpression lj when lj.Table is TableExpression te && IsOwnedTable(te):
                    aliases.Add(te.Alias);
                    break;
                case InnerJoinExpression ij when ij.Table is TableExpression te && IsOwnedTable(te):
                    aliases.Add(te.Alias);
                    break;

                // Lateral-join subquery: LEFT JOIN (SELECT … FROM owned …) AS s
                case LeftJoinExpression lj when lj.Table is SelectExpression inner && IsAllOwnedTablesSelect(inner):
                    if (inner.Alias != null) aliases.Add(inner.Alias);
                    break;
                case InnerJoinExpression ij when ij.Table is SelectExpression inner && IsAllOwnedTablesSelect(inner):
                    if (inner.Alias != null) aliases.Add(inner.Alias);
                    break;
            }
        }
        return aliases;
    }

    /// <inheritdoc />
    protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
    {
        switch (sqlUnaryExpression.OperatorType)
        {
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(decimal):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(float):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(float):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(uint):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(int):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(short):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(ushort):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(ulong):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(long):
            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(double):
            {
                Sql.Append("TONUMBER(");
                var requiresParentheses = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresParentheses)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresParentheses)
                {
                    Sql.Append(")");
                }

                Sql.Append(")");
                break;
            }

            case ExpressionType.Convert when sqlUnaryExpression.Type == typeof(string):
            {
                Sql.Append("TOSTRING(");
                var requiresParentheses = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresParentheses)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresParentheses)
                {
                    Sql.Append(")");
                }

                Sql.Append(")");
                break;
            }

            case ExpressionType.Convert
                when sqlUnaryExpression.Type == typeof(bool):
            {
                Sql.Append("TOBOOLEAN(");
                var requiresParentheses = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresParentheses)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresParentheses)
                {
                    Sql.Append(")");
                }

                Sql.Append(")");
                break;
            }

            case ExpressionType.Not
                when sqlUnaryExpression.Type == typeof(bool):
            {
                switch (sqlUnaryExpression.Operand)
                {
                    case InExpression inExpression:
                        GenerateIn(inExpression, negated: true);
                        break;

                    case ExistsExpression existsExpression:
                        GenerateExists(existsExpression, negated: true);
                        break;

                    case LikeExpression likeExpression:
                        GenerateLike(likeExpression, negated: true);
                        break;

                    default:
                        Sql.Append("NOT (");
                        Visit(sqlUnaryExpression.Operand);
                        Sql.Append(")");
                        break;
                }

                break;
            }

            case ExpressionType.Not:
            {
                Sql.Append("~");

                var requiresBrackets = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append(")");
                }

                break;
            }

            case ExpressionType.Equal:
            {
                var requiresBrackets = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append(")");
                }

                Sql.Append(" IS NULL");
                break;
            }

            case ExpressionType.NotEqual:
            {
                var requiresBrackets = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append(")");
                }

                Sql.Append(" IS NOT NULL");
                break;
            }

            case ExpressionType.Negate:
            {
                Sql.Append("-");
                var requiresBrackets = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append("(");
                }

                Visit(sqlUnaryExpression.Operand);
                if (requiresBrackets)
                {
                    Sql.Append(")");
                }

                break;
            }
        }

        return sqlUnaryExpression;
    }

    protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
    {
        Sql
            .Append(sqlConstantExpression.TypeMapping!.GenerateSqlLiteral(sqlConstantExpression.Value));

        return sqlConstantExpression;
    }

    protected override void GenerateRootCommand(Expression queryExpression)
    {
        switch (queryExpression)
        {
            case SelectExpression selectExpression:
                GenerateTagsHeaderComment(selectExpression.Tags);

                if (selectExpression.IsNonComposedFromSql())
                {
                    GenerateFromSql((FromSqlExpression)selectExpression.Tables[0]);
                }
                else
                {
                    VisitSelect(selectExpression);
                }

                break;

            case UpdateExpression updateExpression:
                GenerateTagsHeaderComment(updateExpression.Tags);
                VisitUpdate(updateExpression);
                break;

            case DeleteExpression deleteExpression:
                GenerateTagsHeaderComment(deleteExpression.Tags);
                VisitDelete(deleteExpression);
                break;

            default:
                base.Visit(queryExpression);
                break;
        }
    }

        /// <inheritdoc />
    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        IDisposable? subQueryIndent = null;
        if (selectExpression.Alias != null)
        {
            Sql.AppendLine("(");
            subQueryIndent =   Sql.Indent();
        }

        if (!TryGenerateWithoutWrappingSelect(selectExpression))
        {
            Sql.Append("SELECT ");

            if (selectExpression.IsDistinct)
            {
                Sql.Append("DISTINCT ");
            }

            GenerateTop(selectExpression);

            if (selectExpression.Projection.Any())
            {
                // Collect aliases of owned-type JOINs that will be suppressed in FROM/JOIN.
                // Columns referencing those aliases are excluded from the SELECT list so N1QL
                // does not see undefined table alias references (e.g. `cm0`.`id` when the
                // contactMethod LEFT JOIN is skipped). The EF Core shaper's baked-in ordinals
                // still expect those slots — they are kept as null placeholders in
                // projectionAliases so CouchbaseDbDataReader returns DBNull for them, which
                // the shaper interprets as "no collection rows". PopulateCollectionNavigations
                // then populates the collection from the embedded JSON array.
                var skippedAliases = CollectOwnedJoinAliases(selectExpression);
                bool IsFromSkippedJoin(ProjectionExpression pe) =>
                    skippedAliases.Count > 0
                    && pe.Expression is ColumnExpression col
                    && skippedAliases.Contains(col.TableAlias);

                if (selectExpression.Projection.Count == 1)
                {
                    var expression = selectExpression.Projection.First().Expression;
                    if (expression is SqlFunctionExpression sqlFunctionExpression)
                    {
                        if (sqlFunctionExpression.Name == "COUNT")
                        {
                            Sql.Append("RAW ");
                        }
                    }
                    else if (expression is ExistsExpression existsExpression)
                    {
                        Sql.Append("RAW ");
                    }
                    else if (expression is CouchbaseUnnestValueExpression)
                    {
                        // A single CouchbaseUnnestValueExpression projection is always the
                        // SelectExpression TranslatePrimitiveCollection built for a primitive
                        // collection -- consumed by the surrounding expression tree as an IN-list
                        // source (.Contains()/.Any(predicate)) or similar set-of-scalars context.
                        // N1QL's default `SELECT expr` wraps each row in an object keyed by an
                        // implicit alias (e.g. `{"p": "Bob"}`), which breaks direct value
                        // comparison against a bare scalar -- confirmed via a live-cluster spike
                        // that `'Bob' IN (SELECT p FROM ...)` never matches without this.
                        // `SELECT RAW expr` projects the bare scalar value instead.
                        Sql.Append("RAW ");
                    }
                    GenerateList(selectExpression.Projection, e => Visit(e));
                }
                else if (selectExpression.Alias == null)
                {
                    // Top-level SELECT: each row becomes a JSON object keyed by alias, so two
                    // projections sharing an effective alias (e.g. a collection Include where the
                    // principal and dependent both expose `rating` / `blogId`) would collide on a
                    // single key. Emit a unique alias for every colliding projection, aligned with
                    // the alias array built in CouchbaseShapedQueryCompilingExpressionVisitor.
                    var uniqueAliases = CouchbaseProjectionAliases.ComputeUnique(selectExpression.Projection);
                    var emitted = new List<(ProjectionExpression Projection, string Alias)>();
                    for (var i = 0; i < selectExpression.Projection.Count; i++)
                    {
                        var projection = selectExpression.Projection[i];
                        if (!IsFromSkippedJoin(projection))
                            emitted.Add((projection, uniqueAliases[i]));
                    }

                    GenerateList(emitted, e =>
                    {
                        // Append AS when the unique alias differs from what the projection would
                        // emit on its own (keeps non-colliding queries byte-identical), OR when the
                        // projection is a META(alias).field column -- N1QL's own implicit naming
                        // for that shape doesn't reliably produce the desired key (see
                        // CouchbaseProjectionAliases.NeedsExplicitAlias).
                        if (e.Alias != CouchbaseProjectionAliases.EffectiveAlias(e.Projection)
                            || CouchbaseProjectionAliases.NeedsExplicitAlias(e.Projection))
                        {
                            Visit(e.Projection.Expression);
                            Sql.Append(AliasSeparator)
                                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(e.Alias));
                        }
                        else
                        {
                            Visit(e.Projection);
                        }
                    });
                }
                else
                {
                    // Sub-query SELECT: its projected column names are referenced by the enclosing
                    // query's join/column expressions, so they must not be renamed. Dedupe by
                    // alias to avoid emitting an identical column twice.
                    var dedupedProjections = new Dictionary<string, ProjectionExpression>();
                    foreach (var expression in selectExpression.Projection)
                    {
                        if (!IsFromSkippedJoin(expression))
                            dedupedProjections.TryAdd(expression.Alias, expression);
                    }

                    GenerateList(dedupedProjections.Values.ToList(), e => Visit(e));
                }
            }
            else
            {
                GenerateEmptyProjection(selectExpression);
            }

            if (selectExpression.Tables.Any())
            {
                Sql.AppendLine().Append("FROM ");

                GenerateList(selectExpression.Tables, e => Visit(e), sql => sql.AppendLine());
            }
            else
            {
                GeneratePseudoFromClause();
            }

            if (selectExpression.Predicate != null)
            {
                Sql.AppendLine().Append("WHERE ");

                Visit(selectExpression.Predicate);
            }

            if (selectExpression.GroupBy.Count > 0)
            {
                Sql.AppendLine().Append("GROUP BY ");

                GenerateList(selectExpression.GroupBy, e => Visit(e));
            }

            if (selectExpression.Having != null)
            {
                Sql.AppendLine().Append("HAVING ");

                Visit(selectExpression.Having);
            }

            GenerateOrderings(selectExpression);
            GenerateLimitOffset(selectExpression);
        }

        if (selectExpression.Alias != null)
        {
            subQueryIndent!.Dispose();

            Sql.AppendLine()
                .Append(")")
                .Append(AliasSeparator)
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(selectExpression.Alias));
        }

        return selectExpression;
    }

    /// <summary>
    /// Renders a scalar subquery (e.g. <c>.Count()</c>/<c>.Sum()</c>/<c>.Max()</c> used in a
    /// comparison) with a trailing <c>[0]</c> array-subscript. Confirmed via a live-cluster spike:
    /// unlike ANSI SQL, N1QL's parenthesized-subquery-as-expression syntax always evaluates to an
    /// ARRAY of the subquery's result rows, even for a single-row/single-column result -- e.g.
    /// <c>(SELECT RAW COUNT(*) FROM t.public_likes AS p) = 3</c> silently never matches, comparing
    /// <c>[3]</c> against <c>3</c>, while the identical query with a trailing <c>[0]</c> correctly
    /// unwraps the one-element array to the bare scalar. <see cref="ExistsExpression"/> and
    /// <see cref="InExpression"/>'s <c>Subquery</c> shape are unaffected -- N1QL gives
    /// <c>EXISTS (...)</c>/<c>... IN (...)</c> their own dedicated boolean semantics that don't
    /// go through this array-valued-expression path (both already render correctly without it).
    /// </summary>
    protected override Expression VisitScalarSubquery(ScalarSubqueryExpression scalarSubqueryExpression)
    {
        Sql.AppendLine("(");
        using (Sql.Indent())
        {
            Visit(scalarSubqueryExpression.Subquery);
        }

        Sql.Append(")[0]");

        return scalarSubqueryExpression;
    }

    protected override void GenerateExists(ExistsExpression existsExpression, bool negated)
    {
        // `.Any(predicate)`/`.Any()` over an OwnsMany navigation translates (via EF Core's own,
        // unmodified RelationalQueryableMethodTranslatingExpressionVisitor.TranslateAny) into a
        // correlated EXISTS subquery whose sole FROM table is the owned type's TableExpression --
        // but VisitTable renders that table as nothing (it's embedded JSON, not a real keyspace),
        // which would otherwise produce an empty-FROM-clause N1QL error. Render it instead as
        // N1QL's ANY...SATISFIES...END over the parent document's array field directly.
        if (existsExpression.Subquery.Tables is [TableExpression tableExpression]
            && TryGetOwnedEntityTypes(tableExpression, out var ownedEntityTypes))
        {
            // A table can carry more than one owned entity type via table-splitting (see
            // TryGetOwnedEntityTypes' remarks) -- try each until one's ownership actually matches
            // this subquery's correlation predicate.
            foreach (var candidate in ownedEntityTypes)
            {
                if (TryRenderOwnedCollectionAny(existsExpression, negated, tableExpression, candidate))
                {
                    return;
                }
            }
        }

        if (negated)
        {
            Sql.Append("NOT ");
        }

        Sql.AppendLine("EXISTS (");

        using (Sql.Indent())
        {
            Visit(existsExpression.Subquery);
        }

        Sql.Append(")");
    }

    /// <summary>
    /// Renders <c>ANY &lt;ownedAlias&gt; IN &lt;parentAlias&gt;.&lt;fieldName&gt; SATISFIES
    /// &lt;predicate&gt; END</c> in place of the correlated <c>EXISTS</c> subquery EF Core built
    /// for `.Any(predicate)`/`.Any()` over a depth-1 OwnsMany navigation. Returns
    /// <see langword="false"/> (writing nothing) if the ownership/navigation can't be resolved, so
    /// the caller falls through to the default (and, for this shape, broken) EXISTS rendering
    /// rather than silently producing wrong SQL.
    /// </summary>
    private bool TryRenderOwnedCollectionAny(
        ExistsExpression existsExpression, bool negated, TableExpression ownedTable, IEntityType ownedEntityType)
    {
        var ownership = ownedEntityType.FindOwnership();
        if (ownership?.PrincipalToDependent == null)
        {
            return false;
        }

        if (!TryStripCorrelation(
                existsExpression.Subquery.Predicate, ownedTable.Alias!, ownership,
                out var residual, out var parentAlias))
        {
            return false;
        }

        var fieldName = CouchbaseProjectionAliases.GetOwnedCollectionFieldName(ownership.PrincipalToDependent, _fieldNamingPolicy);
        var helper = Dependencies.SqlGenerationHelper;

        if (negated)
        {
            Sql.Append("NOT (");
        }

        Sql.Append("ANY ")
            .Append(helper.DelimitIdentifier(ownedTable.Alias!))
            .Append(" IN ")
            .Append(helper.DelimitIdentifier(parentAlias!))
            .Append(".")
            .Append(helper.DelimitIdentifier(fieldName))
            .Append(" SATISFIES ");

        if (residual == null)
        {
            Sql.Append("true");
        }
        else
        {
            var previous = _ownedAnyAlias;
            _ownedAnyAlias = ownedTable.Alias;
            try
            {
                Visit(residual);
            }
            finally
            {
                _ownedAnyAlias = previous;
            }
        }

        Sql.Append(" END");

        if (negated)
        {
            Sql.Append(")");
        }

        return true;
    }

    /// <summary>
    /// Splits <paramref name="predicate"/> into the correlation conjunct(s) EF Core added when
    /// expanding the owned-collection navigation (<c>child.FK = parent.PK</c>, or
    /// <c>child.FK IS NOT NULL AND child.FK = parent.PK</c> per FK property when any FK/PK
    /// property is nullable) and everything else (the user's original `.Any(predicate)`
    /// condition, or <see langword="null"/> for predicate-less `.Any()`). Also recovers the
    /// correlated outer query's alias as a side effect of removing the correlation conjunct(s) --
    /// the surviving side of each stripped comparison, by construction, refers to it.
    /// </summary>
    private static bool TryStripCorrelation(
        SqlExpression? predicate,
        string ownedAlias,
        IForeignKey ownership,
        out SqlExpression? residual,
        out string? parentAlias)
    {
        residual = null;
        parentAlias = null;

        if (predicate == null)
        {
            return false;
        }

        var conjuncts = new List<SqlExpression>();
        FlattenAndAlso(predicate, conjuncts);

        var fkProperties = ownership.Properties;
        var pkProperties = ownership.PrincipalKey.Properties;

        for (var i = 0; i < fkProperties.Count; i++)
        {
            var fkColumn = fkProperties[i].GetColumnName();
            var pkColumn = pkProperties[i].GetColumnName();

            // Plain shape: a single Equal comparing the FK column (owned side) against the PK
            // column (outer/parent side) -- the common case (OwnsMany shadow FKs are non-nullable
            // by convention).
            var plainIndex = conjuncts.FindIndex(c => IsCorrelationEqual(c, ownedAlias, fkColumn, pkColumn, out _));
            if (plainIndex >= 0)
            {
                IsCorrelationEqual(conjuncts[plainIndex], ownedAlias, fkColumn, pkColumn, out var foundAlias);
                parentAlias ??= foundAlias;
                conjuncts.RemoveAt(plainIndex);
                continue;
            }

            // Null-guarded shape: a separate `fkColumn IS NOT NULL` conjunct alongside the Equal,
            // for a nullable FK/PK property.
            var notNullIndex = conjuncts.FindIndex(c => IsColumnNotNull(c, ownedAlias, fkColumn));
            var equalIndex = conjuncts.FindIndex(c => IsCorrelationEqual(c, ownedAlias, fkColumn, pkColumn, out _));
            if (notNullIndex >= 0 && equalIndex >= 0)
            {
                IsCorrelationEqual(conjuncts[equalIndex], ownedAlias, fkColumn, pkColumn, out var foundAlias);
                parentAlias ??= foundAlias;
                // Remove the higher index first so the lower index stays valid.
                if (notNullIndex > equalIndex)
                {
                    conjuncts.RemoveAt(notNullIndex);
                    conjuncts.RemoveAt(equalIndex);
                }
                else
                {
                    conjuncts.RemoveAt(equalIndex);
                    conjuncts.RemoveAt(notNullIndex);
                }
                continue;
            }

            // A required FK/PK property pair's correlation conjunct wasn't found at all -- this
            // isn't the shape we expect from ExpandOwnedNavigation, so don't guess.
            return false;
        }

        if (parentAlias == null)
        {
            return false;
        }

        residual = conjuncts.Count == 0
            ? null
            : conjuncts.Aggregate((left, right) => new SqlBinaryExpression(
                ExpressionType.AndAlso, left, right, typeof(bool), left.TypeMapping));

        return true;
    }

    private static void FlattenAndAlso(SqlExpression expression, List<SqlExpression> conjuncts)
    {
        if (expression is SqlBinaryExpression { OperatorType: ExpressionType.AndAlso } binary)
        {
            FlattenAndAlso(binary.Left, conjuncts);
            FlattenAndAlso(binary.Right, conjuncts);
        }
        else
        {
            conjuncts.Add(expression);
        }
    }

    private static bool IsCorrelationEqual(
        SqlExpression expression, string ownedAlias, string fkColumn, string pkColumn, out string? parentAlias)
    {
        parentAlias = null;
        if (expression is not SqlBinaryExpression { OperatorType: ExpressionType.Equal } binary)
        {
            return false;
        }

        if (binary.Left is ColumnExpression left && binary.Right is ColumnExpression right)
        {
            if (left.TableAlias == ownedAlias && left.Name == fkColumn && right.TableAlias != ownedAlias && right.Name == pkColumn)
            {
                parentAlias = right.TableAlias;
                return true;
            }

            if (right.TableAlias == ownedAlias && right.Name == fkColumn && left.TableAlias != ownedAlias && left.Name == pkColumn)
            {
                parentAlias = left.TableAlias;
                return true;
            }
        }

        return false;
    }

    private static bool IsColumnNotNull(SqlExpression expression, string ownedAlias, string columnName)
        => expression is SqlBinaryExpression { OperatorType: ExpressionType.NotEqual } binary
           && ((binary.Left is ColumnExpression lc && lc.TableAlias == ownedAlias && lc.Name == columnName && binary.Right is SqlConstantExpression { Value: null })
               || (binary.Right is ColumnExpression rc && rc.TableAlias == ownedAlias && rc.Name == columnName && binary.Left is SqlConstantExpression { Value: null }));

    /// <summary>
    /// Dispatch point for provider-specific extension expression types (<see cref="ExpressionType.Extension"/>).
    /// <see cref="CouchbaseUnnestExpression"/>, <see cref="CouchbaseUnnestValueExpression"/>, and
    /// <see cref="CouchbaseArrayIndexExpression"/> are the only ones this provider introduces;
    /// anything else falls through to the base dispatcher (which itself handles all of EF Core's
    /// own stock extension node types -- <see cref="TableExpression"/>, <see cref="ColumnExpression"/>,
    /// etc.).
    /// </summary>
    protected override Expression VisitExtension(Expression node)
        => node switch
        {
            CouchbaseUnnestExpression unnestExpression => GenerateUnnest(unnestExpression),
            CouchbaseUnnestValueExpression valueExpression => GenerateUnnestValue(valueExpression),
            CouchbaseArrayIndexExpression arrayIndexExpression => GenerateArrayIndex(arrayIndexExpression),
            _ => base.VisitExtension(node),
        };

    /// <summary>
    /// Renders a bare reference to a <see cref="CouchbaseUnnestExpression"/>'s alias -- see that
    /// expression type's remarks for why this, not a named <c>alias.value</c> column reference, is
    /// the correct projection for N1QL's <c>FROM arrayExpr AS alias</c> (confirmed via a
    /// live-cluster spike: the SQLite-style <c>alias.value</c> shape silently evaluates to MISSING
    /// for every row and returns an empty result set, no error).
    /// </summary>
    private Expression GenerateUnnestValue(CouchbaseUnnestValueExpression valueExpression)
    {
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(valueExpression.Alias));
        return valueExpression;
    }

    /// <summary>
    /// Renders <c>&lt;arrayExpr&gt; AS &lt;alias&gt;</c> as the sole FROM-clause term of the
    /// wrapping <see cref="SelectExpression"/> that
    /// <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor.TranslatePrimitiveCollection"/>
    /// built for a scalar primitive-collection property. Deliberately omits the literal <c>UNNEST</c>
    /// keyword: confirmed via a live-cluster spike that this Couchbase Server version rejects
    /// <c>UNNEST</c> as the primary/sole FROM-term ("UNNEST (reserved word)") -- it's only valid as a
    /// secondary/join-like term following a real keyspace-ref. A bare correlated array expression as
    /// the primary FROM-term (no <c>UNNEST</c> keyword at all) is confirmed to work correctly,
    /// including inside a correlated subquery, which is the only shape
    /// <see cref="CouchbaseUnnestExpression"/> is ever used in. No positional/ordinal alias either --
    /// see that method's remarks for why (this Couchbase Server version also rejects
    /// <c>UNNEST ... AT alias</c>).
    /// </summary>
    private Expression GenerateUnnest(CouchbaseUnnestExpression unnestExpression)
    {
        var helper = Dependencies.SqlGenerationHelper;

        Visit(unnestExpression.ArrayExpression);
        Sql.Append(" AS ").Append(helper.DelimitIdentifier(unnestExpression.Alias!));

        return unnestExpression;
    }

    /// <summary>
    /// Renders N1QL's native array-subscript syntax (<c>arrayExpr[indexExpr]</c>), built by
    /// <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor.TranslateElementAtOrDefault"/>
    /// for <c>.ElementAt(i)</c>/indexer access over a scalar primitive-collection property.
    /// </summary>
    private Expression GenerateArrayIndex(CouchbaseArrayIndexExpression arrayIndexExpression)
    {
        Visit(arrayIndexExpression.Array);
        Sql.Append("[");
        Visit(arrayIndexExpression.Index);
        Sql.Append("]");

        return arrayIndexExpression;
    }

    protected override Expression VisitTable(TableExpression tableExpression)
    {
        //NOTE: TableExpression is a sealed class so cannot be overridden without
        //bring it inside this assembly which then requires the TableExpressionBase to
        //be moved into this assembly as Alias field is internal.

        // Skip owned type tables — they are embedded in their owner's document and have no
        // independent Couchbase keyspace.
        if (IsOwnedTable(tableExpression))
            return tableExpression;

        // Parse once per distinct table name and cache.
        var keyspace = _tableNameCache.GetOrAdd(
            tableExpression.Name,
            static name => CouchbaseKeyspace.Parse(name));

        // Use the provider's SqlGenerationHelper to quote and escape each keyspace segment.
        // DelimitIdentifier splits on '.' and applies EscapeIdentifier (backtick-doubling)
        // per segment, keeping this path consistent with all other identifier quoting in
        // the provider and safe against names that might contain backtick characters.
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(keyspace.ToString()))
            .Append(AliasSeparator)
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableExpression.Alias));

        return tableExpression;
    }

    protected override Expression VisitColumn(ColumnExpression columnExpression)
    {
        var helper = Dependencies.SqlGenerationHelper;
        var metaField = CouchbaseProjectionAliases.GetMetaField(columnExpression);
        if (metaField != null)
        {
            // META() takes no keyspace parameter when the alias is the one being indexed/queried --
            // https://docs.couchbase.com/server/current/n1ql/n1ql-language-reference/indexing-meta-info.html
            // Deliberately no alias appended here -- CouchbaseProjectionAliases.EffectiveAlias
            // already knows this renders as the lowercase field name (e.g. "id"), not the
            // property's own name, so the outer projection-list loop in VisitSelect adds an
            // explicit AS whenever the property name differs, exactly like any other projection.
            Sql.Append("META(")
                .Append(helper.DelimitIdentifier(columnExpression.TableAlias))
                .Append(").")
                .Append(metaField.ToLowerInvariant());
            return columnExpression;
        }

        // Inside an owned-collection ANY(...)'s SATISFIES clause (see
        // TryRenderOwnedCollectionAny), a column bound to the owned alias refers to a property of
        // an array element embedded in JSON -- those are written under the policy-converted name
        // (e.g. "title", not "Title"), unlike every other column this generator renders.
        var propertyName = columnExpression.TableAlias == _ownedAnyAlias
            ? _fieldNamingPolicy?.ConvertName(columnExpression.Name) ?? columnExpression.Name
            : columnExpression.Name;

        Sql.Append(helper.DelimitIdentifier(columnExpression.TableAlias))
            .Append(".")
            .Append(helper.DelimitIdentifier(propertyName));

        return columnExpression;
    }

    public override IRelationalCommand GetCommand(Expression queryExpression)
    {
        var command = base.GetCommand(queryExpression);
        return command;
    }

    protected override string GetOperator(SqlBinaryExpression binaryExpression)
    {
        ArgumentNullException.ThrowIfNull(binaryExpression);

        return binaryExpression.OperatorType == ExpressionType.Add
            && binaryExpression.Type == typeof(string)
                ? " || "
                : base.GetOperator(binaryExpression);
    }

    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        ArgumentNullException.ThrowIfNull(selectExpression);

        if (selectExpression.Limit != null
            || selectExpression.Offset != null)
        {
            Sql.AppendLine()
                .Append("LIMIT ");

            Visit(
                selectExpression.Limit
                ?? new SqlConstantExpression(-1, selectExpression.Offset!.TypeMapping));

            if (selectExpression.Offset != null)
            {
                Sql.Append(" OFFSET ");

                Visit(selectExpression.Offset);
            }
        }
    }

    protected override void GenerateSetOperationOperand(SetOperationBase setOperation, SelectExpression operand)
    {
        ArgumentNullException.ThrowIfNull(setOperation);
        ArgumentNullException.ThrowIfNull(operand);

        Visit(operand);
    }

    private void GenerateFromSql(FromSqlExpression fromSqlExpression)
    {
        var sql = fromSqlExpression.Sql;
        string[]? substitutions;

        switch (fromSqlExpression.Arguments)
        {
            case ConstantExpression { Value: CompositeRelationalParameter compositeRelationalParameter }:
            {
                var subParameters = compositeRelationalParameter.RelationalParameters;
                substitutions = new string[subParameters.Count];
                for (var i = 0; i < subParameters.Count; i++)
                {
                    substitutions[i] = Dependencies.SqlGenerationHelper.GenerateParameterNamePlaceholder(subParameters[i].InvariantName);
                }

                Sql.AddParameter(compositeRelationalParameter);
                break;
            }

            case ConstantExpression { Value: object[] constantValues }:
            {
                substitutions = new string[constantValues.Length];
                for (var i = 0; i < constantValues.Length; i++)
                {
                    var value = constantValues[i];
                    if (value is RawRelationalParameter rawRelationalParameter)
                    {
                        substitutions[i] = Dependencies.SqlGenerationHelper.GenerateParameterNamePlaceholder(rawRelationalParameter.InvariantName);
                       Sql.AddParameter(rawRelationalParameter);
                    }
                    else if (value is SqlConstantExpression sqlConstantExpression)
                    {
                        substitutions[i] = sqlConstantExpression.TypeMapping!.GenerateSqlLiteral(sqlConstantExpression.Value);
                    }
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(fromSqlExpression),
                    fromSqlExpression.Arguments,
                    RelationalStrings.InvalidFromSqlArguments(
                        fromSqlExpression.Arguments.GetType(),
                        fromSqlExpression.Arguments is ConstantExpression constantExpression
                            ? constantExpression.Value?.GetType()
                            : null));
        }

        // ReSharper disable once CoVariantArrayConversion
        // InvariantCulture not needed since substitutions are all strings
        sql = string.Format(sql, substitutions);

        Sql.AppendLines(sql);
    }

    protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
    {
        // N1QL's AVG() natively returns a double for any numeric input. Strip the TONUMBER()
        // that EF Core injects for int/long arguments — it is unnecessary in SQL++ and can
        // trigger a CouchbaseParsingException on some server versions (NCBC-3891).
        //
        // Guard: only strip the Convert node when the target type is numeric (the same set that
        // VisitSqlUnary routes to TONUMBER). ExpressionType.Convert is also used for TOSTRING and
        // TOBOOLEAN — those must be left intact even though AVG(TOSTRING/TOBOOLEAN) is not valid
        // standard SQL, because a custom translator could theoretically produce such a tree.
        if (sqlFunctionExpression.IsBuiltIn
            && sqlFunctionExpression.Name == "AVG"
            && sqlFunctionExpression.Arguments is { Count: 1 } avgArguments
            && avgArguments[0] is SqlUnaryExpression
                { OperatorType: ExpressionType.Convert } numericCast
            && IsNumericType(numericCast.Type))
        {
            Sql.Append("AVG(");
            Visit(numericCast.Operand);
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        if (sqlFunctionExpression.IsBuiltIn)
        {
            if (sqlFunctionExpression.Instance != null)
            {
                Visit(sqlFunctionExpression.Instance);
                Sql.Append(".");
            }

            // EF Core's own SqlExpressionFactory.Coalesce() builds a builtin "COALESCE" function
            // for '??' -- N1QL has no COALESCE function at all (it has IFMISSINGORNULL/IFNULL/NVL),
            // so this would otherwise reach the server as invalid SQL++ and fail only at
            // query-execution time, not at translation time. IFMISSINGORNULL is the semantically
            // correct choice, not just a renaming: a Couchbase document field can be genuinely
            // MISSING (absent from the JSON entirely), not just JSON null, and IFMISSINGORNULL is
            // the only one of the three that treats both the same way '??' does in C#. EF Core
            // flattens a chain (`a ?? b ?? c`) into a single N-ary COALESCE(a, b, c) call rather
            // than nesting it, and IFMISSINGORNULL also accepts an arbitrary number of arguments,
            // so a straight name substitution is correct regardless of argument count.
            Sql.Append(sqlFunctionExpression.Name == "COALESCE" ? "IFMISSINGORNULL" : sqlFunctionExpression.Name);
        }
        else
        {
            if (!string.IsNullOrEmpty(sqlFunctionExpression.Schema))
            {
                Sql
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(sqlFunctionExpression.Schema))
                    .Append(".");
            }

            Sql
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(sqlFunctionExpression.Name));
        }

        if (!sqlFunctionExpression.IsNiladic)
        {
            Sql.Append("(");
            GenerateList(sqlFunctionExpression.Arguments, e => Visit(e));
            Sql.Append(")");
        }

        return sqlFunctionExpression;
    }

    /// <summary>
    ///     Generates SQL for the IN expression.
    /// </summary>
    /// <param name="inExpression">The expression to visit.</param>
    /// <param name="negated">Whether the given <paramref name="inExpression" /> is negated.</param>
    protected override void GenerateIn(InExpression inExpression, bool negated)
    {
        Visit(inExpression.Item);

        // N1QL's array-literal IN syntax (`x IN [1, 2, 3]`) only applies to a flat Values list --
        // a Subquery-shaped InExpression (e.g. `.Contains()` over a queryable source, including
        // this provider's own UNNEST-backed primitive collections) is a real correlated/uncorrelated
        // SELECT and must use standard parenthesized subquery-IN syntax instead. Confirmed
        // pre-existing: `InExpression.Values`/`.Subquery` are mutually-exclusive alternate shapes of
        // the same node type, and this method previously always bracketed both the same way --
        // harmless as long as nothing produced the Subquery shape, until primitive collections did.
        if (inExpression.Values is not null)
        {
            Sql.Append(negated ? " NOT IN [" : " IN [");
            GenerateList(inExpression.Values, e => Visit(e));
            Sql.Append("]");
        }
        else
        {
            Sql.Append(negated ? " NOT IN (" : " IN (");
            Sql.AppendLine();

            using (Sql.Indent())
            {
                Visit(inExpression.Subquery);
            }

            Sql.AppendLine();
            Sql.Append(")");
        }
    }

    private void GenerateList<T>(
        IReadOnlyList<T> items,
        Action<T> generationAction,
        Action<IRelationalCommandBuilder>? joinAction = null)
    {
        joinAction ??= (isb => isb.Append(", "));

        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                joinAction(Sql);
            }

            generationAction(items[i]);
        }
    }

    /// <summary>
    /// Returns <c>true</c> for CLR types that <see cref="VisitSqlUnary"/> maps to
    /// <c>TONUMBER()</c> — the same exhaustive list used in that switch statement.
    /// Used to narrow the AVG-stripping guard so that Convert-to-string and
    /// Convert-to-bool cases are not accidentally removed (NCBC-3891).
    /// </summary>
    private static bool IsNumericType(Type type)
        => type == typeof(double)
        || type == typeof(float)
        || type == typeof(decimal)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(short)
        || type == typeof(ushort);
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