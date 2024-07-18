using System.Linq.Expressions;
using System.Text;
using Couchbase.Core.Utils;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Primitives;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGenerator : QuerySqlGenerator
{
    public CouchbaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies) : base(dependencies)
    {
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

    protected override void GenerateExists(ExistsExpression existsExpression, bool negated)
    {
        Sql.Append(" RAW ");
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
        //Not ideal but we need to reformat the table (really the bucket+scope+collection) to N1QL
        //format of `bucket`.`scope`.`collection`. TableExpression is sealed and not injectable AFAIK.
        var contextId = tableExpression.Name.Split('.');
        var contextBuilder = new StringBuilder();
        for (var i = 0; i < contextId.Length; i++)
        {
            contextBuilder.Append(contextId[i].EscapeIfRequired());
            if (i < contextId.Length-1)
            {
                contextBuilder.Append('.');
            }
        }

        Sql.Append(contextBuilder.ToString())
            .Append(AliasSeparator)
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableExpression.Alias));

        return tableExpression;
    }

    protected override Expression VisitColumn(ColumnExpression columnExpression)
    {
        var helper = Dependencies.SqlGenerationHelper;
        Sql.Append(helper.DelimitIdentifier(columnExpression.TableAlias))
            .Append(".")
            .Append(helper.DelimitIdentifier(columnExpression.Name.ToLower()));//TODO ToLower is a hack this should be done during entity translation
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

            //make the response scalar with no extra JSON
            if (sqlFunctionExpression.Name == "COUNT")
            {
                Sql.Append(" RAW ");
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