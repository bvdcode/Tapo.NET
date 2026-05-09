using System;

namespace Tapo;

/// <summary>
/// Base class for every exception raised by Tapo.NET. Library callers can
/// catch this type when they need to lump together every protocol-level
/// failure.
/// </summary>
public class TapoException : Exception
{
    public TapoException() { }

    public TapoException(string message) : base(message) { }

    public TapoException(string message, Exception? inner) : base(message, inner) { }
}
