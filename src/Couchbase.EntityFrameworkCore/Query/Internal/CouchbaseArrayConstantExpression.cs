using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// An expression representing an inline N1QL array literal (<c>[e1, e2, ...]</c>) in a SQL tree --
/// used to build the single array argument N1QL's <c>ARRAY_MIN</c>/<c>ARRAY_MAX</c> require (unlike
/// C#'s <c>Math.Min(a, b)</c>/<c>Math.Max(a, b)</c>, which take two scalar arguments). Mirrors
/// <see cref="CouchbaseArrayIndexExpression"/>'s shape -- a small, provider-specific
/// <see cref="SqlExpression"/> subtype rendered directly by <c>CouchbaseQuerySqlGenerator</c>,
/// rather than trying to force this shape through a stock <see cref="SqlConstantExpression"/> or
/// <see cref="SqlFunctionExpression"/>.
/// </summary>
public class CouchbaseArrayConstantExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public virtual IReadOnlyList<SqlExpression> Elements { get; }

    public CouchbaseArrayConstantExpression(
        IReadOnlyList<SqlExpression> elements,
        Type type,
        RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        Elements = elements;
    }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        SqlExpression[]? newElements = null;
        for (var i = 0; i < Elements.Count; i++)
        {
            var newElement = (SqlExpression)visitor.Visit(Elements[i]);
            if (newElement != Elements[i] && newElements == null)
            {
                newElements = new SqlExpression[Elements.Count];
                for (var j = 0; j < i; j++)
                {
                    newElements[j] = Elements[j];
                }
            }

            if (newElements != null)
            {
                newElements[i] = newElement;
            }
        }

        return newElements == null ? this : Update(newElements);
    }

    public virtual CouchbaseArrayConstantExpression Update(IReadOnlyList<SqlExpression> elements)
        => elements.Count == Elements.Count && elements.Zip(Elements, (a, b) => a == b).All(x => x)
            ? this
            : new CouchbaseArrayConstantExpression(elements, Type, TypeMapping);

#pragma warning disable EF9100 // RelationalExpressionQuotingUtilities is evaluation-purposes-only -- matches the same pattern CouchbaseArrayIndexExpression uses for this exact purpose.
    public override Expression Quote()
        => Expression.New(
            _quotingConstructor ??= typeof(CouchbaseArrayConstantExpression).GetConstructor(
                [typeof(IReadOnlyList<SqlExpression>), typeof(Type), typeof(RelationalTypeMapping)])!,
            Expression.NewArrayInit(typeof(SqlExpression), Elements.Select(e => e.Quote())),
            Expression.Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
#pragma warning restore EF9100

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append("[");
        for (var i = 0; i < Elements.Count; i++)
        {
            if (i > 0)
            {
                expressionPrinter.Append(", ");
            }

            expressionPrinter.Visit(Elements[i]);
        }

        expressionPrinter.Append("]");
    }

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is CouchbaseArrayConstantExpression other && Equals(other));

    private bool Equals(CouchbaseArrayConstantExpression other)
        => base.Equals(other) && Elements.SequenceEqual(other.Elements);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        foreach (var element in Elements)
        {
            hash.Add(element);
        }

        return hash.ToHashCode();
    }
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
