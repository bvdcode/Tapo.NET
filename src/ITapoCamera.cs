using System;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Devices;
using Tapo.Download;
using Tapo.MediaStream;
using Tapo.Recordings;
using Tapo.Snapshots;

namespace Tapo;

/// <summary>
/// Aggregate root that exposes everything a caller normally needs from a Tapo
/// camera: control-channel authentication, recording metadata, device-level
/// commands and a high-throughput downloader.
/// </summary>
public interface ITapoCamera : IAsyncDisposable
{
    /// <summary>Hashing algorithm currently negotiated with the camera.</summary>
    EncryptionMethod EncryptionMethod { get; }

    /// <summary>Recordings client — list, search, download metadata.</summary>
    IRecordingsClient Recordings { get; }

    /// <summary>Device commands — basic info, time, LED, privacy mode, motion detection, SD card, reboot.</summary>
    IDeviceClient Device { get; }

    /// <summary>Performs login if necessary. Idempotent.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a configured <see cref="IVideoDownloader"/> with default downloader settings.</summary>
    IVideoDownloader CreateDownloader();

    /// <summary>Returns a configured <see cref="IVideoDownloader"/> using the supplied downloader settings.</summary>
    IVideoDownloader CreateDownloader(VideoDownloaderOptions downloaderOptions);

    /// <summary>Returns a snapshot client that can capture single JPEG frames.</summary>
    ISnapshotClient CreateSnapshotClient();

    /// <summary>Creates a raw <see cref="IMediaSession"/> when the caller needs full control over the media protocol.</summary>
    IMediaSession CreateMediaSession(MediaSessionOptions? overrides = null);
}
