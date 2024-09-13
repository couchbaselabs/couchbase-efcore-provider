using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Couchbase.Core.Utils;
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
    private readonly INamedBucketProvider _namedBucketProvider;

    public CouchbaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, INamedBucketProvider namedBucketProvider) : base(dependencies)
    {
        _namedBucketProvider = namedBucketProvider;
    }

    protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
    {
        var value = sqlConstantExpression.Value;
        var type = value.GetType();
        if (type.Name == "Int32")
        {
            Sql
                .Append(sqlConstantExpression.Value.ToString());
        }
        else
        {
            Sql
                .Append(sqlConstantExpression.TypeMapping!.GenerateSqlLiteral(sqlConstantExpression.Value)); 
        }
        
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
                    if (selectExpression.Projection.First().Expression is SqlFunctionExpression sqlFunctionExpression)
                    {
                        if (sqlFunctionExpression.Name == "COUNT")
                        {
                            Sql.Append(" RAW ");
                        }
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
        var scopeAndCollection = tableExpression.Name.Split('.');
        var keyspaceBuilder = new StringBuilder();
        keyspaceBuilder.Append(_namedBucketProvider.BucketName.EscapeIfRequired()).Append('.');
        for (var i = 0; i < scopeAndCollection.Length; i++)
        {
            keyspaceBuilder.Append(scopeAndCollection[i].EscapeIfRequired());
            if (i < scopeAndCollection.Length-1)
            {
                keyspaceBuilder.Append('.');
            }
        }

        Sql.Append(keyspaceBuilder.ToString())
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