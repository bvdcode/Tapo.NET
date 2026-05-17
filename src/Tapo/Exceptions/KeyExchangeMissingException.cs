namespace Tapo;

/// <summary>
/// Raised when the media-stream handshake completes without the
/// <c>Key-Exchange</c> header that supplies the AES key material.
/// </summary>
public sealed class KeyExchangeMissingException : TapoException
{
    /// <summary>Creates the exception with the standard descriptive message.</summary>
    public KeyExchangeMissingException()
        : base("Server reply does not contain the required Key-Exchange header.") { }
}
