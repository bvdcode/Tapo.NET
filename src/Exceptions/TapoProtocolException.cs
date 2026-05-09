namespace Tapo;

/// <summary>
/// Raised when an HTTP-class protocol error happens on either the control or
/// the media-stream channel — e.g. an unexpected status code or a malformed
/// response.
/// </summary>
public sealed class TapoProtocolException : TapoException
{
    public TapoProtocolException(string message) : base(message) { }

    public TapoProtocolException(int statusCode)
        : base($"HTTP request returned {statusCode} status code.")
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
