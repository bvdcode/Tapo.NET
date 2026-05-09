namespace Tapo;

/// <summary>
/// Raised when the media-stream handshake completes without the
/// <c>Key-Exchange</c> header that supplies the AES key material.
/// </summary>
public sealed class KeyExchangeMissingException : TapoException
{
    public KeyExchangeMissingException()
        : base("Server reply does not contain the required Key-Exchange header.") { }
}
