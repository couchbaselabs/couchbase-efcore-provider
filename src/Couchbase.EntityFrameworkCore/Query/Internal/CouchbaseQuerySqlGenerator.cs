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

    public CouchbaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies) : base(dependencies)
    {
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
        if (leftJoinExpression.Table is TableExpression tableExpression && IsOwnedTable(tableExpression))
            return leftJoinExpression;

        return base.VisitLeftJoin(leftJoinExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitInnerJoin(InnerJoinExpression innerJoinExpression)
    {
        // Skip INNER JOINs for owned types — they are embedded in their owner's document
        // and have no independent keyspace.
        if (innerJoinExpression.Table is TableExpression tableExpression && IsOwnedTable(tableExpression))
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
                    GenerateList(selectExpression.Projection, e => Visit(e));
                }
                else
                {
                    var dedupedProjections = new Dictionary<string, ProjectionExpression>();
                    foreach (var expression in selectExpression.Projection)
                    {
                        dedupedProjections.TryAdd(expression.Alias, expression);
                    }

                    GenerateList(dedupedProjections.Values.ToList(), e => Visit(e));
                }
                // GenerateList(selectExpression.Projection, e => Visit(e));
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

    protected override void GenerateExists(ExistsExpression existsExpression, bool negated)
    {
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
        Sql.Append(helper.DelimitIdentifier(columnExpression.TableAlias))
            .Append(".")
            .Append(helper.DelimitIdentifier(columnExpression.Name));

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
                ?? new SqlConstantExpression(Expression.Constant(-1), selectExpression.Offset!.TypeMapping));

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
            && sqlFunctionExpression.Arguments.Count == 1
            && sqlFunctionExpression.Arguments[0] is SqlUnaryExpression
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

            Sql.Append(sqlFunctionExpression.Name);
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
        Sql.Append(negated ? " NOT IN [" : " IN [");

        if (inExpression.Values is not null)
        {
            GenerateList(inExpression.Values, e => Visit(e));
        }
        else
        {
            Sql.AppendLine();

            using (Sql.Indent())
            {
                Visit(inExpression.Subquery);
            }

            Sql.AppendLine();
        }

        Sql.Append("]");
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