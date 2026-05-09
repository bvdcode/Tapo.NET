namespace Tapo.Control;

/// <summary>
/// Connection settings for the camera's control channel — the HTTPS endpoint
/// used to log in, list recordings and get the user id.
/// </summary>
public sealed class ControlChannelOptions
{
    /// <summary>Camera IP or hostname.</summary>
    public string Host { get; init; } = default!;

    /// <summary>Control-channel HTTPS port. Defaults to 443.</summary>
    public int Port { get; init; } = 443;

    /// <summary>Local account username — for Tapo cameras this is always <c>"admin"</c>.</summary>
    public string Username { get; init; } = "admin";

    /// <summary>Local account password (the one you set when commissioning the camera).</summary>
    public string Password { get; init; } = default!;

    /// <summary>Cloud account password — used by the media stream encryption layer.</summary>
    public string CloudPassword { get; init; } = default!;

    /// <summary>Optional child-device id. Set when the camera is a child of a hub/NVR.</summary>
    public string? ChildDeviceId { get; init; }

    /// <summary>Request timeout in milliseconds.</summary>
    public int TimeoutMilliseconds { get; init; } = 15_000;

    /// <summary>
    /// Skip TLS chain validation — required because cameras serve self-signed
    /// certificates. Defaults to <see langword="true"/>.
    /// </summary>
    public bool TrustAnyServerCertificate { get; init; } = true;
}
