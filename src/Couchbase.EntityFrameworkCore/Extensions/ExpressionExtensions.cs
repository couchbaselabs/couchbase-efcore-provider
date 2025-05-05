// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

#nullable enable

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Couchbase.EntityFrameworkCore.Extensions;

[DebuggerStepThrough]
internal static class ExpressionExtensions
{
    public static bool IsNullConstantExpression(this Expression expression)
        => RemoveConvert(expression) is ConstantExpression { Value: null };

    public static LambdaExpression UnwrapLambdaFromQuote(this Expression expression)
        => (LambdaExpression)(expression is UnaryExpression unary && expression.NodeType == ExpressionType.Quote
            ? unary.Operand
            : expression);

    [return: NotNullIfNotNull("expression")]
    public static Expression? UnwrapTypeConversion(this Expression? expression, out Type? convertedType)
    {
        convertedType = null;
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
               } unaryExpression)
        {
            expression = unaryExpression.Operand;
            if (unaryExpression.Type != typeof(object) // Ignore object conversion
                && !unaryExpression.Type.IsAssignableFrom(expression.Type)) // Ignore casting to base type/interface
            {
                convertedType = unaryExpression.Type;
            }
        }

        return expression;
    }

    private static Expression RemoveConvert(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unaryExpression
            ? RemoveConvert(unaryExpression.Operand)
            : expression;

    public static T GetConstantValue<T>(this Expression expression)
        => expression is ConstantExpression constantExpression
            ? (T)constantExpression.Value!
            : throw new InvalidOperationException();
}
