using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseParameterBasedSqlProcessor(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
    RelationalParameterBasedSqlProcessorParameters parameters)
    : RelationalParameterBasedSqlProcessor(dependencies, parameters)
{
    protected override Expression ProcessSqlNullability(
        Expression queryExpression, ParametersCacheDecorator parametersDecorator)
        => new CouchbaseSqlNullabilityProcessor(Dependencies, Parameters).Process(queryExpression, parametersDecorator);
}
