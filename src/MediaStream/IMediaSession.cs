using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.MediaStream;

/// <summary>
/// A live connection to a Tapo camera's media-stream endpoint. The interface
/// exists so that callers (and tests) can substitute a fake without touching
/// real sockets.
/// </summary>
public interface IMediaSession : IAsyncDisposable
{
    /// <summary>True after the TLS-less handshake has succeeded.</summary>
    bool IsStarted { get; }

    /// <summary>
    /// Currently configured window size. Update with <see cref="SetWindowSize"/>
    /// before the first <see cref="TransceiveAsync"/> call to influence
    /// download throughput.
    /// </summary>
    int WindowSize { get; }

    /// <summary>Performs the TPAP handshake. Idempotent.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the window size before the first <see cref="TransceiveAsync"/> call.</summary>
    void SetWindowSize(int windowSize);

    /// <summary>
    /// Sends a single request to the camera and yields every response part
    /// dispatched onto the request's sequence/session.
    /// </summary>
    /// <param name="payload">The raw payload bytes (typically a UTF-8 JSON request).</param>
    /// <param name="mimeType">MIME type of the payload.</param>
    /// <param name="sessionId">Existing session id when continuing an established session; <see langword="null"/> for a brand-new request.</param>
    /// <param name="encrypt">Whether the payload should be AES-encrypted before sending.</param>
    /// <param name="noDataTimeoutMilliseconds">Time after which the iterator stops if the camera goes silent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<MediaResponse> TransceiveAsync(
        byte[] payload,
        string mimeType = MimeTypes.Json,
        long? sessionId = null,
        bool encrypt = false,
        int noDataTimeoutMilliseconds = 10_000,
        CancellationToken cancellationToken = default);
}
