using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseParameterBasedSqlProcessor(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
    RelationalParameterBasedSqlProcessorParameters parameters)
    : RelationalParameterBasedSqlProcessor(dependencies, parameters)
{
    /// <inheritdoc />
    public override Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
        => new ConsistentCountResolver(parametersDecorator).Visit(base.Process(queryExpression, parametersDecorator));

    protected override Expression ProcessSqlNullability(
        Expression queryExpression, ParametersCacheDecorator parametersDecorator)
        => new CouchbaseSqlNullabilityProcessor(Dependencies, Parameters).Process(queryExpression, parametersDecorator);

    /// <summary>
    /// Replaces each <see cref="ConsistentCountMarker"/> with its decision for these parameter
    /// values, as the <see cref="bool"/> that <see cref="CouchbaseQuerySqlGenerator"/> reads. This
    /// runs when the query runs, after the whole query is translated.
    /// </summary>
    private sealed class ConsistentCountResolver(ParametersCacheDecorator parametersDecorator) : ExpressionVisitor
    {
        [return: NotNullIfNotNull(nameof(node))]
        public override Expression? Visit(Expression? node)
            => node is TableExpression table
               && table.FindAnnotation(CouchbaseQueryHintAnnotationNames.ConsistentCount)?.Value is ConsistentCountMarker marker
                ? TableHintAnnotations.SetHintAnnotation(
                    table, CouchbaseQueryHintAnnotationNames.ConsistentCount, marker.RequiresIndexCount(parametersDecorator))
                : base.Visit(node);
    }
}
