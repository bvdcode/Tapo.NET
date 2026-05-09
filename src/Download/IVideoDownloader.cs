using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.Download;

/// <summary>
/// Streams an SD-card recording from the camera to an arbitrary destination.
/// </summary>
public interface IVideoDownloader
{
    /// <summary>
    /// Downloads <paramref name="request"/> and writes the raw MPEG-TS bytes
    /// into <paramref name="destination"/>. The stream is left open so the
    /// caller controls the file lifetime.
    /// </summary>
    Task DownloadAsync(
        VideoDownloadRequest request,
        Stream destination,
        IProgress<VideoDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
