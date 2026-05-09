namespace Tapo.MediaStream;

/// <summary>Well-known media-stream HTTP headers.</summary>
internal static class MediaStreamHeaders
{
    public const string ContentType = "Content-Type";
    public const string ContentLength = "Content-Length";
    public const string Authorization = "Authorization";
    public const string Connection = "Connection";
    public const string Host = "Host";

    public const string SessionId = "X-Session-Id";
    public const string DataSequence = "X-Data-Sequence";
    public const string DataReceived = "X-Data-Received";
    public const string DataPts = "X-Data-PTS";
    public const string IsIFrame = "X-If-IFrame";
    public const string IsEncrypt = "X-If-Encrypt";
    public const string DataWindowSize = "X-Data-Window-Size";
    public const string ClientUuid = "X-Client-UUID";

    public const string WwwAuthenticate = "WWW-Authenticate";
    public const string KeyExchange = "Key-Exchange";
}
