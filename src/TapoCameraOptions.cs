namespace Tapo;

/// <summary>
/// Top-level configuration for a Tapo camera. The same values feed into both
/// the control channel (login, listing recordings) and the media stream
/// (downloading recordings).
/// </summary>
public sealed class TapoCameraOptions
{
    /// <summary>Camera IP or hostname. Required.</summary>
    public string Host { get; init; } = default!;

    /// <summary>Local account username. Defaults to <c>"admin"</c>.</summary>
    public string Username { get; init; } = "admin";

    /// <summary>Local account password (the password you set on the camera itself).</summary>
    public string Password { get; init; } = default!;

    /// <summary>
    /// Cloud account password — same as the one you use in the Tapo mobile
    /// app. The media channel derives its AES key from this value. In most
    /// home setups <see cref="Password"/> and <see cref="CloudPassword"/>
    /// are the same string.
    /// </summary>
    public string CloudPassword { get; init; } = default!;

    /// <summary>Fallback secret for cameras with media encryption disabled.</summary>
    public string SuperSecretKey { get; init; } = string.Empty;

    /// <summary>Control-channel HTTPS port. Defaults to 443.</summary>
    public int ControlPort { get; init; } = 443;

    /// <summary>Media-stream TCP port. Defaults to 8800.</summary>
    public int StreamPort { get; init; } = 8800;

    /// <summary>
    /// Default download window size (number of media packets the camera may
    /// push before requiring an acknowledgement). 50 is a safe default; bump
    /// it for known-good links to maximise throughput.
    /// </summary>
    public int DefaultWindowSize { get; init; } = 50;

    /// <summary>Skip TLS chain validation on the control channel.</summary>
    public bool TrustAnyServerCertificate { get; init; } = true;
}
