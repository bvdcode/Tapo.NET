using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.Snapshots;

/// <summary>
/// Pulls a single JPEG frame from the camera over the media stream — the
/// modern equivalent of opening RTSP just to grab a still image.
/// </summary>
public interface ISnapshotClient
{
    /// <summary>Captures a single JPEG frame and returns the raw bytes.</summary>
    Task<byte[]> CaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Captures a single JPEG frame and writes it into <paramref name="destination"/>.</summary>
    Task CaptureToAsync(Stream destination, CancellationToken cancellationToken = default);
}
