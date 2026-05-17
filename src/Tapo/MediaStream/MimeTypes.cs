namespace Tapo.MediaStream;

/// <summary>Well-known media-stream MIME types.</summary>
public static class MimeTypes
{
    /// <summary><c>application/json</c> — control messages exchanged over the media stream.</summary>
    public const string Json = "application/json";

    /// <summary><c>video/mp2t</c> — MPEG-TS payload carrying H.264/AAC video data.</summary>
    public const string MpegTs = "video/mp2t";

    /// <summary><c>image/jpeg</c> — single-frame JPEG snapshots.</summary>
    public const string Jpeg = "image/jpeg";
}
