using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Extends the base nullability processor to handle <see cref="CouchbaseArrayIndexExpression"/> --
/// the base <see cref="SqlNullabilityProcessor"/> throws "Unhandled expression" for any custom
/// <see cref="SqlExpression"/> subtype it doesn't recognize (confirmed: this is the exact wall
/// SQLite's own <c>GlobExpression</c>/<c>RegexpExpression</c> hit, solved via the same
/// <c>VisitCustomSqlExpression</c> override this class copies).
/// </summary>
public class CouchbaseSqlNullabilityProcessor : SqlNullabilityProcessor
{
    public CouchbaseSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression, bool allowOptimizedExpansion, out bool nullable)
        => sqlExpression switch
        {
            CouchbaseArrayIndexExpression arrayIndexExpression
                => VisitArrayIndex(arrayIndexExpression, allowOptimizedExpansion, out nullable),
            CouchbaseUnnestValueExpression valueExpression
                => VisitUnnestValue(valueExpression, out nullable),
            CouchbaseArrayConstantExpression arrayConstantExpression
                => VisitArrayConstant(arrayConstantExpression, allowOptimizedExpansion, out nullable),
            _ => base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable)
        };

    protected virtual SqlExpression VisitUnnestValue(CouchbaseUnnestValueExpression valueExpression, out bool nullable)
    {
        nullable = valueExpression.IsNullable;
        return valueExpression;
    }

    protected virtual SqlExpression VisitArrayIndex(
        CouchbaseArrayIndexExpression arrayIndexExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        var array = Visit(arrayIndexExpression.Array, out _);
        var index = Visit(arrayIndexExpression.Index, out _);

        // N1QL returns MISSING (falsy) for an out-of-range/negative subscript rather than
        // erroring, so this is always potentially null regardless of its operands' nullability.
        nullable = true;

        return arrayIndexExpression.Update(array, index);
    }

    protected virtual SqlExpression VisitArrayConstant(
        CouchbaseArrayConstantExpression arrayConstantExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        var elements = new SqlExpression[arrayConstantExpression.Elements.Count];
        for (var i = 0; i < elements.Length; i++)
        {
            elements[i] = Visit(arrayConstantExpression.Elements[i], out _);
        }

        // The array literal itself ([e1, e2, ...]) is never null, regardless of whether any
        // individual element is -- an element being NULL just makes that slot contain NULL, not
        // the whole array. Distinct from CouchbaseArrayIndexExpression above, where indexing INTO
        // an array can itself produce MISSING.
        nullable = false;

        return arrayConstantExpression.Update(elements);
    }
}
