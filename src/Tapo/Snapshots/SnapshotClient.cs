using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Internal;
using Tapo.MediaStream;

namespace Tapo.Snapshots;

/// <summary>
/// Default <see cref="ISnapshotClient"/>. Owns a short-lived
/// <see cref="IMediaSession"/> for the duration of a single capture and
/// returns the first JPEG part the camera sends back.
/// </summary>
public sealed class SnapshotClient : ISnapshotClient
{
    private readonly Func<MediaSessionOptions, IMediaSession> _sessionFactory;
    private readonly MediaSessionOptions _baseOptions;

    /// <summary>Creates a snapshot client.</summary>
    public SnapshotClient(MediaSessionOptions baseOptions, Func<MediaSessionOptions, IMediaSession>? sessionFactory = null)
    {
        Throw.IfNull(baseOptions);
        _baseOptions = baseOptions;
        _sessionFactory = sessionFactory ?? (opts => new MediaSession(opts));
    }

    /// <inheritdoc />
    public async Task<byte[]> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _sessionFactory(_baseOptions);
        await session.StartAsync(cancellationToken).ConfigureAwait(false);

        var payload = Encoding.UTF8.GetBytes(
            "{\"type\":\"request\",\"seq\":1,\"params\":{\"preview\":{\"channels\":[0],\"resolutions\":[\"HD\"],\"snapShotType\":[\"normal\"]},\"method\":\"get\"}}");

        await foreach (var response in session.TransceiveAsync(
                           payload,
                           MimeTypes.Json,
                           noDataTimeoutMilliseconds: 15_000,
                           cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            try
            {
                if (string.Equals(response.MimeType, MimeTypes.Jpeg, StringComparison.OrdinalIgnoreCase))
                {
                    return response.Payload;
                }
            }
            finally
            {
                response.JsonDocument?.Dispose();
            }
        }

        throw new TapoProtocolException("Camera did not return a JPEG snapshot before the stream ended.");
    }

    /// <inheritdoc />
    public async Task CaptureToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Destination must be writable.", nameof(destination));

        var bytes = await CaptureAsync(cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
