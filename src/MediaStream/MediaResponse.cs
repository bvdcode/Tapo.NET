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

    public long? Sequence { get; }

    public long? SessionId { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public bool Encrypted { get; }

    public string MimeType { get; }

    /// <summary>Decrypted bytes of this part. Borrowed from the channel-internal pool — copy if you need to keep it.</summary>
    public byte[] Payload { get; }

    /// <summary>Parsed JSON, when <see cref="MimeType"/> is <c>application/json</c>. Otherwise <see langword="null"/>.</summary>
    public JsonDocument? JsonDocument { get; }
}
