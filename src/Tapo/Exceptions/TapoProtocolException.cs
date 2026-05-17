namespace Tapo;

/// <summary>
/// Raised when an HTTP-class protocol error happens on either the control or
/// the media-stream channel — e.g. an unexpected status code or a malformed
/// response.
/// </summary>
public sealed class TapoProtocolException : TapoException
{
    /// <summary>Creates the exception with the supplied descriptive message.</summary>
    public TapoProtocolException(string message) : base(message) { }

    /// <summary>Creates the exception for an unexpected HTTP <paramref name="statusCode"/>.</summary>
    public TapoProtocolException(int statusCode)
        : base($"HTTP request returned {statusCode} status code.")
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code that triggered the failure, when applicable.</summary>
    public int? StatusCode { get; }
}
