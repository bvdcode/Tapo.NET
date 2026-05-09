// Polyfills for compiler-required types missing from netstandard2.1.
// They allow us to use C# 9+ records, init-only properties and the
// [CallerArgumentExpression] attribute without targeting net5.0+.

#pragma warning disable IDE0079
#pragma warning disable SA1402, SA1649, IDE0073, IDE0130

namespace System.Runtime.CompilerServices
{
    /// <summary>Marker required by the C# compiler to recognise <c>init</c>-only setters.</summary>
    internal static class IsExternalInit
    {
    }

    /// <summary>Backport of .NET 6's CallerArgumentExpressionAttribute.</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }

        public string ParameterName { get; }
    }
}
