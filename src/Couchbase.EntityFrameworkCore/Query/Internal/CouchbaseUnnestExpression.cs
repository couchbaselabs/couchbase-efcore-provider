using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// An expression that represents N1QL's <c>UNNEST</c> clause in a SQL tree, used to expand a
/// JSON array (a scalar "primitive collection" property, e.g. <c>List&lt;string&gt;</c>) into a
/// virtual rowset of one row per array element -- Couchbase's equivalent of SQL Server's
/// <c>OPENJSON</c>/SQLite's <c>json_each</c>. Modeled directly on
/// <see href="https://github.com/dotnet/efcore/blob/main/src/EFCore.Sqlite.Core/Query/Internal/SqlExpressions/JsonEachExpression.cs">
/// JsonEachExpression</see>.
/// </summary>
/// <remarks>
/// Unlike <c>json_each(...)</c>/<c>OPENJSON(...)</c> (self-contained, callable table-valued
/// functions), N1QL's <c>UNNEST</c> is a clause, not a function -- it has no parentheses.
/// Extends <see cref="TableExpressionBase"/> directly rather than
/// <see cref="TableValuedFunctionExpression"/> for that reason.
/// <para>
/// Carries no positional/ordinal alias -- confirmed empirically that this Couchbase Server
/// version rejects <c>UNNEST ... AT alias</c> as a syntax error ("AT (reserved word)") in every
/// position tried. Element indexing (<c>.ElementAt(i)</c>/<c>arr[i]</c>) is therefore NOT
/// implemented via Skip+Limit over an ordered unnest rowset (the SqlServer/SQLite pattern) --
/// instead <see cref="CouchbaseQueryableMethodTranslatingExpressionVisitor"/> overrides
/// <c>TranslateElementAtOrDefault</c> to render N1QL's native <c>arr[index]</c> subscript
/// directly via <see cref="CouchbaseArrayIndexExpression"/>. This expression type is used only
/// for the order-independent operators (<c>.Contains()</c>/<c>.Any(predicate)</c>/<c>.Count()</c>/
/// <c>.Where()</c>), which don't need positional information at all.
/// </para>
/// </remarks>
public class CouchbaseUnnestExpression : TableExpressionBase
{
    /// <summary>The array-valued expression being unnested (typically a <see cref="ColumnExpression"/>).</summary>
    public virtual SqlExpression ArrayExpression { get; }

    public CouchbaseUnnestExpression(string alias, SqlExpression arrayExpression)
        : base(alias)
    {
        ArrayExpression = arrayExpression;
    }

    private CouchbaseUnnestExpression(
        string alias, SqlExpression arrayExpression, IReadOnlyDictionary<string, IAnnotation>? annotations)
        : base(alias, annotations)
    {
        ArrayExpression = arrayExpression;
    }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedArrayExpression = (SqlExpression)visitor.Visit(ArrayExpression);
        return Update(visitedArrayExpression);
    }

    public virtual CouchbaseUnnestExpression Update(SqlExpression arrayExpression)
        => arrayExpression == ArrayExpression
            ? this
            : new CouchbaseUnnestExpression(Alias!, arrayExpression);

    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
    {
        var newArrayExpression = (SqlExpression)cloningExpressionVisitor.Visit(ArrayExpression);
        var clone = new CouchbaseUnnestExpression(alias!, newArrayExpression);

        foreach (var annotation in GetAnnotations())
        {
            clone.AddAnnotation(annotation.Name, annotation.Value);
        }

        return clone;
    }

    public override CouchbaseUnnestExpression WithAlias(string newAlias)
        => new(newAlias, ArrayExpression);

    protected override TableExpressionBase WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new CouchbaseUnnestExpression(Alias!, ArrayExpression, annotations);

    private static ConstructorInfo? _quotingConstructor;

    public override Expression Quote()
        => Expression.New(
            _quotingConstructor ??= typeof(CouchbaseUnnestExpression).GetConstructor(
                [typeof(string), typeof(SqlExpression)])!,
            Expression.Constant(Alias, typeof(string)),
            ArrayExpression.Quote());

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        // No "UNNEST " prefix -- matches CouchbaseQuerySqlGenerator.GenerateUnnest, which
        // deliberately omits the keyword (see the remarks above and on that method for why).
        expressionPrinter.Visit(ArrayExpression);
        expressionPrinter.Append(" AS ").Append(Alias!);
        PrintAnnotations(expressionPrinter);
    }

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is CouchbaseUnnestExpression other && Equals(other));

    private bool Equals(CouchbaseUnnestExpression other)
        => base.Equals(other) && ArrayExpression.Equals(other.ArrayExpression);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), ArrayExpression);
}
