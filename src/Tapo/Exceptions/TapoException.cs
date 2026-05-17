using System;

namespace Tapo;

/// <summary>
/// Base class for every exception raised by Tapo.NET. Library callers can
/// catch this type when they need to lump together every protocol-level
/// failure.
/// </summary>
public class TapoException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public TapoException() { }

    /// <summary>Creates the exception with the supplied message.</summary>
    public TapoException(string message) : base(message) { }

    /// <summary>Creates the exception with the supplied message and inner exception.</summary>
    public TapoException(string message, Exception? inner) : base(message, inner) { }
}
