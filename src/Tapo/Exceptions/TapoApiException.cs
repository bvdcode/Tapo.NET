namespace Tapo;

/// <summary>
/// Raised when the camera answers a control-channel request with a non-zero
/// <c>error_code</c>.
/// </summary>
public sealed class TapoApiException : TapoException
{
    /// <summary>Creates the exception for a non-zero camera error code.</summary>
    public TapoApiException(int errorCode, string method, string? mappedName)
        : base($"Tapo API call '{method}' failed with code {errorCode} ({mappedName ?? "unknown"}).")
    {
        ErrorCode = errorCode;
        Method = method;
        MappedName = mappedName;
    }

    /// <summary>Numeric <c>error_code</c> returned by the camera.</summary>
    public int ErrorCode { get; }

    /// <summary>Name of the API method the camera was asked to execute.</summary>
    public string Method { get; }

    /// <summary>Friendly name for <see cref="ErrorCode"/> if one is known.</summary>
    public string? MappedName { get; }
}
