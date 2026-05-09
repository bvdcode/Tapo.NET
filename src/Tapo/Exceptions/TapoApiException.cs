namespace Tapo;

/// <summary>
/// Raised when the camera answers a control-channel request with a non-zero
/// <c>error_code</c>.
/// </summary>
public sealed class TapoApiException : TapoException
{
    public TapoApiException(int errorCode, string method, string? mappedName)
        : base($"Tapo API call '{method}' failed with code {errorCode} ({mappedName ?? "unknown"}).")
    {
        ErrorCode = errorCode;
        Method = method;
        MappedName = mappedName;
    }

    public int ErrorCode { get; }

    public string Method { get; }

    public string? MappedName { get; }
}
