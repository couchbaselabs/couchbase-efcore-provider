using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Represents a reference to the element value itself produced by a
/// <see cref="CouchbaseUnnestExpression"/>'s alias (e.g. the <c>p</c> in
/// <c>FROM t.public_likes AS p</c>) -- used as the sole projection of the
/// <see cref="SelectExpression"/> <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor.TranslatePrimitiveCollection"/>
/// builds for a scalar primitive-collection property.
/// </summary>
/// <remarks>
/// Unlike SQLite's <c>json_each</c> (a table-valued function whose output rows have real
/// named columns -- <c>key</c>, <c>value</c>, <c>type</c>, etc. -- so a plain
/// <see cref="ColumnExpression"/> named <c>"value"</c> is the correct projection there),
/// N1QL's bare <c>FROM arrayExpr AS alias</c> binds <c>alias</c> directly to each array
/// ELEMENT, not to a wrapper row-object with a <c>.value</c> field. Confirmed via a live-cluster
/// spike: projecting/filtering on <c>alias.value</c> silently evaluates to MISSING for every row
/// (since a scalar element like a string has no such field), producing an empty result set with
/// no error -- the correct reference is the bare alias itself.
/// </remarks>
public class CouchbaseUnnestValueExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public virtual string Alias { get; }
    public virtual bool IsNullable { get; }

    public CouchbaseUnnestValueExpression(string alias, bool isNullable, Type type, RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        Alias = alias;
        IsNullable = isNullable;
    }

    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

#pragma warning disable EF9100 // RelationalExpressionQuotingUtilities is evaluation-purposes-only -- matches the same pattern EF Core's own SqlExpression subclasses (e.g. SqlUnaryExpression) use internally for this exact purpose.
    public override Expression Quote()
        => Expression.New(
            _quotingConstructor ??= typeof(CouchbaseUnnestValueExpression).GetConstructor(
                [typeof(string), typeof(bool), typeof(Type), typeof(RelationalTypeMapping)])!,
            Expression.Constant(Alias, typeof(string)),
            Expression.Constant(IsNullable),
            Expression.Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
#pragma warning restore EF9100

    protected override void Print(ExpressionPrinter expressionPrinter)
        => expressionPrinter.Append(Alias);

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is CouchbaseUnnestValueExpression other && Equals(other));

    private bool Equals(CouchbaseUnnestValueExpression other)
        => base.Equals(other) && Alias == other.Alias && IsNullable == other.IsNullable;

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Alias, IsNullable);
}
