using System.Collections.Generic;

namespace Tapo.MediaStream;

/// <summary>
/// Configuration for a single <see cref="MediaSession"/>. Most defaults match
/// the camera firmware's expectations and rarely need to be tweaked — the
/// notable exception is <see cref="WindowSize"/>, which trades raw download
/// throughput against link stability.
/// </summary>
public sealed class MediaSessionOptions
{
    /// <summary>Camera IP or hostname. Required.</summary>
    public string Host { get; init; } = default!;

    /// <summary>Media-stream TCP port. The Tapo firmware listens on 8800.</summary>
    public int Port { get; init; } = 8800;

    /// <summary>Username advertised in HTTP Digest. The firmware accepts <c>"admin"</c>.</summary>
    public string Username { get; init; } = "admin";

    /// <summary>Cloud account password. Used to derive both the Digest credential and the AES key.</summary>
    public string CloudPassword { get; init; } = default!;

    /// <summary>
    /// Fallback secret used only when the camera disables media encryption — i.e.
    /// when the Key-Exchange header reports <c>username="none"</c>. Empty by default.
    /// </summary>
    public string SuperSecretKey { get; init; } = string.Empty;

    /// <summary>Hash algorithm for the password digest. Detected via the control channel.</summary>
    public EncryptionMethod EncryptionMethod { get; init; } = EncryptionMethod.Md5;

    /// <summary>
    /// Number of media packets the camera is allowed to push before it must
    /// receive an acknowledgement. Larger values yield faster downloads but a
    /// flaky link may stall; the reference Python implementation defaults to
    /// 50 for live streams and up to 500 for downloads.
    /// </summary>
    public int WindowSize { get; init; } = 50;

    /// <summary>Optional query string parameters appended to the <c>POST /stream</c> request line.</summary>
    public IReadOnlyDictionary<string, string>? QueryParameters { get; init; }

    /// <summary>Connect/IO timeouts in milliseconds. Zero means infinite.</summary>
    public int ConnectTimeoutMilliseconds { get; init; } = 10_000;

    /// <summary>Per-read/per-write timeout on the media-stream socket. Zero means infinite.</summary>
    public int IoTimeoutMilliseconds { get; init; } = 30_000;
}
