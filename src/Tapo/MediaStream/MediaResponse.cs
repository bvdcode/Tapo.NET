using System.Collections.Generic;
using System.Text.Json;

namespace Tapo.MediaStream;

/// <summary>
/// One multipart part received from the camera over the media stream. Mirrors
/// the Python <c>HttpMediaResponse</c> structure but uses byte arrays for
/// payloads to avoid copying through intermediate strings.
/// </summary>
public sealed class MediaResponse
{
    /// <summary>Creates a new multipart media response.</summary>
    public MediaResponse(
        long? sequence,
        long? sessionId,
        IReadOnlyDictionary<string, string> headers,
        bool encrypted,
        string mimeType,
        byte[] payload,
        JsonDocument? jsonDocument)
    {
        Sequence = sequence;
        SessionId = sessionId;
        Headers = headers;
        Encrypted = encrypted;
        MimeType = mimeType;
        Payload = payload;
        JsonDocument = jsonDocument;
    }

    /// <summary>Sequence number echoed by the camera for the request, if any.</summary>
    public long? Sequence { get; }

    /// <summary>Stream-session identifier the camera assigned to this response, if any.</summary>
    public long? SessionId { get; }

    /// <summary>Raw multipart part headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary><see langword="true"/> if the body was AES-encrypted on the wire and decrypted in-place.</summary>
    public bool Encrypted { get; }

    /// <summary>Content-Type of <see cref="Payload"/> (see <see cref="MimeTypes"/>).</summary>
    public string MimeType { get; }

    /// <summary>Decrypted bytes of this part. Borrowed from the channel-internal pool — copy if you need to keep it.</summary>
    public byte[] Payload { get; }

    /// <summary>Parsed JSON, when <see cref="MimeType"/> is <c>application/json</c>. Otherwise <see langword="null"/>.</summary>
    public JsonDocument? JsonDocument { get; }
}
