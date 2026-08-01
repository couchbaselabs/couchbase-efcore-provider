using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// An expression representing N1QL's native array-subscript syntax (<c>arrayExpr[indexExpr]</c>)
/// in a SQL tree -- used for <c>.ElementAt(i)</c>/indexer access over a scalar primitive-collection
/// property. Confirmed via a live-cluster spike that this Couchbase Server version rejects
/// <c>UNNEST ... AT posAlias</c> (no positional-ordinal syntax available), so unlike SQL Server's
/// <c>JSON_VALUE(json, '$[i]')</c>/SQLite's <c>-&gt;&gt;</c> (both <em>optional</em> fast-path
/// optimizations over an ordered rowset), direct array indexing is the ONLY mechanism this
/// provider has for correct, deterministic element access -- see
/// <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/>'s <c>TranslateElementAtOrDefault</c>
/// override.
/// </summary>
public class CouchbaseArrayIndexExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public virtual SqlExpression Array { get; }
    public virtual SqlExpression Index { get; }

    public CouchbaseArrayIndexExpression(
        SqlExpression array,
        SqlExpression index,
        Type type,
        RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        Array = array;
        Index = index;
    }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newArray = (SqlExpression)visitor.Visit(Array);
        var newIndex = (SqlExpression)visitor.Visit(Index);
        return Update(newArray, newIndex);
    }

    public virtual CouchbaseArrayIndexExpression Update(SqlExpression array, SqlExpression index)
        => array == Array && index == Index
            ? this
            : new CouchbaseArrayIndexExpression(array, index, Type, TypeMapping);

#pragma warning disable EF9100 // RelationalExpressionQuotingUtilities is evaluation-purposes-only -- matches the same pattern EF Core's own SqlExpression subclasses (e.g. SqlUnaryExpression) use internally for this exact purpose.
    public override Expression Quote()
        => Expression.New(
            _quotingConstructor ??= typeof(CouchbaseArrayIndexExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(Type), typeof(RelationalTypeMapping)])!,
            Array.Quote(),
            Index.Quote(),
            Expression.Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
#pragma warning restore EF9100

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Array);
        expressionPrinter.Append("[");
        expressionPrinter.Visit(Index);
        expressionPrinter.Append("]");
    }

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is CouchbaseArrayIndexExpression other && Equals(other));

    private bool Equals(CouchbaseArrayIndexExpression other)
        => base.Equals(other) && Array.Equals(other.Array) && Index.Equals(other.Index);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Array, Index);
}
