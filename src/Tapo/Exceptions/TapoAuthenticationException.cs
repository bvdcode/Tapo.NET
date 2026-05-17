namespace Tapo;

/// <summary>
/// Raised when the camera rejects the supplied credentials, sets a temporary
/// suspension or returns an authentication-class error code.
/// </summary>
public sealed class TapoAuthenticationException : TapoException
{
    /// <summary>Creates the exception with the supplied message.</summary>
    public TapoAuthenticationException(string message) : base(message) { }
}
