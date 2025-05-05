// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCore.Utils;

#nullable enable

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

[DebuggerStepThrough]
internal static class Check
{
    [ContractAnnotation("value:null => halt")]
    [return: NotNull]
    public static T NotNull<T>([NoEnumeration] [AllowNull] [NotNull] T value, [InvokerParameterName] string parameterName)
    {
        if (value is null)
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentNullException(parameterName);
        }

        return value;
    }

    [ContractAnnotation("value:null => halt")]
    public static IReadOnlyList<T> NotEmpty<T>(
        [NotNull] IReadOnlyList<T>? value,
        [InvokerParameterName] string parameterName)
    {
        NotNull(value, parameterName);

        if (value.Count == 0)
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentException(AbstractionsStrings.CollectionArgumentIsEmpty(parameterName));
        }

        return value;
    }

    [ContractAnnotation("value:null => halt")]
    public static string NotEmpty([NotNull] string? value, [InvokerParameterName] string parameterName)
    {
        if (value is null)
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentNullException(parameterName);
        }

        if (value.Trim().Length == 0)
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentException(AbstractionsStrings.ArgumentIsEmpty(parameterName));
        }

        return value;
    }

    public static string? NullButNotEmpty(string? value, [InvokerParameterName] string parameterName)
    {
        if (value is not null && value.Length == 0)
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentException(AbstractionsStrings.ArgumentIsEmpty(parameterName));
        }

        return value;
    }

    public static IReadOnlyList<T> HasNoNulls<T>(
        [NotNull] IReadOnlyList<T>? value,
        [InvokerParameterName] string parameterName)
        where T : class
    {
        NotNull(value, parameterName);

        if (value.Any(e => e == null))
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentException(parameterName);
        }

        return value;
    }

    public static IReadOnlyList<string> HasNoEmptyElements(
        [NotNull] IReadOnlyList<string>? value,
        [InvokerParameterName] string parameterName)
    {
        NotNull(value, parameterName);

        if (value.Any(s => string.IsNullOrWhiteSpace(s)))
        {
            NotEmpty(parameterName, nameof(parameterName));

            throw new ArgumentException(AbstractionsStrings.CollectionArgumentHasEmptyElements(parameterName));
        }

        return value;
    }

    [Conditional("DEBUG")]
    public static void DebugAssert([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new UnreachableException($"Check.DebugAssert failed: {message}");
        }
    }

    [Conditional("DEBUG")]
    [DoesNotReturn]
    public static void DebugFail(string message)
        => throw new UnreachableException($"Check.DebugFail failed: {message}");
}
