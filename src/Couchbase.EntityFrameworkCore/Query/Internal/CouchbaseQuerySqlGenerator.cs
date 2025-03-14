using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Text;
using Couchbase.Core.Utils;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGenerator : QuerySqlGenerator
{
    private readonly ConcurrentDictionary<string, Keyspace> _tableNameCache = new();

    public CouchbaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies) : base(dependencies)
    {
    }

     /// <inheritdoc />
    protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
    {
        switch (sqlUnaryExpression.OperatorType)
        {
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
                    Sql.Append("RAW ");
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

    private class Keyspace
    {
        private readonly string _originalName;
        private readonly string _originalAlias;
        private readonly string _alias;
        private readonly string _name;

        public Keyspace(string originalName, string originalAlias)
        {
            _originalName = originalName;
            _originalAlias = originalAlias;

            //first split apart the keyspace and extract the alias from the collection
            var splitName = originalName.Split('.');
            if(splitName.Length != 3) throw ExceptionHelper.InvalidKeyspaceFormatOrMissingCollection(splitName.FirstOrDefault());
            _alias = splitName[2].FirstOrDefault().ToString().ToLowerInvariant();

            //if the original alias has an ordinal index add it to the index
            var splitAlias = _originalAlias.ToArray();
            if(splitAlias.Length == 2)
            {
               _alias += splitAlias[1].ToString().ToLowerInvariant();
            }

            //next apply the delimiters into a new string: `bucket`.`scope`.`collection`
            //note that the order was swapped so that the TableExpression will use the
            //correct character for the alias - the collection name. Sometime in the future
            //we may want to bring the TableExpression into this project and modify its internals
            var keyspaceBuilder = new StringBuilder();
            keyspaceBuilder.Append(splitName[1].EscapeIfRequired());
            keyspaceBuilder.Append('.');
            keyspaceBuilder.Append(splitName[2].EscapeIfRequired());
            keyspaceBuilder.Append('.');
            keyspaceBuilder.Append(splitName[0].EscapeIfRequired());
            _name = keyspaceBuilder.ToString();
        }

        public string Name
        {
            [DebuggerStepThrough] get => _name;
        }

        public string Alias
        {
            [DebuggerStepThrough] get => _alias;
        }
    }

    protected override Expression VisitTable(TableExpression tableExpression)
    {
        //NOTE: TableExpression is a sealed class so cannot be overridden without
        //bring it inside this assembly which then requires the TableExpressionBase to
        //be moved into this assembly as Alias field is internal.
        var keyspace = _tableNameCache.GetOrAdd(
            tableExpression.Name, key => new Keyspace(key, tableExpression.Alias));

        Sql.Append(keyspace.Name)
            .Append(AliasSeparator)
            //.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(keyspace.Alias));
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableExpression.Alias));

        return tableExpression;
    }

    protected override Expression VisitColumn(ColumnExpression columnExpression)
    {
        //NOTE: TableExpression is a sealed class so cannot be overridden without
        //bring it inside this assembly which then requires the TableExpressionBase to
        //be moved into this assembly as Alias field is internal.

        string alias = columnExpression.TableAlias;
       /* if (columnExpression.Table is TableExpression tableExpression)
        {
            tableExpression = (TableExpression)columnExpression.Table;
            var keyspace = _tableNameCache.GetOrAdd(
                tableExpression.Name, key => new Keyspace(key, tableExpression.Alias));
            alias = keyspace.Alias;
        }*/

        var helper = Dependencies.SqlGenerationHelper;
        Sql.Append(helper.DelimitIdentifier(alias))
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
}