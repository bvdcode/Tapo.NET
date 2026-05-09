using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tapo.Internal;

/// <summary>
/// Inlined argument validation helpers that return early so callers do not pay
/// for stack frames in the happy path.
/// </summary>
internal static class Throw
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNull<T>(
        [NotNull] T? argument,
        [CallerArgumentExpression("argument")] string? paramName = null)
        where T : class
    {
        if (argument is null)
        {
            ThrowArgumentNull(paramName);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNullOrEmpty(
        [NotNull] string? argument,
        [CallerArgumentExpression("argument")] string? paramName = null)
    {
        if (string.IsNullOrEmpty(argument))
        {
            ThrowArgumentNullOrEmpty(paramName);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNegative(
        long value,
        [CallerArgumentExpression("value")] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
        }
    }

    [DoesNotReturn]
    private static void ThrowArgumentNull(string? paramName)
        => throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    private static void ThrowArgumentNullOrEmpty(string? paramName)
        => throw new ArgumentException("Value cannot be null or empty.", paramName);
}
