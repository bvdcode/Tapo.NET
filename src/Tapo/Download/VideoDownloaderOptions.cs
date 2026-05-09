namespace Tapo.Download;

/// <summary>
/// Tunables that govern <see cref="VideoDownloader"/> throughput. The defaults
/// were picked to maximise SD-card pull rate on healthy LANs without flooring
/// the camera's response loop.
/// </summary>
public sealed class VideoDownloaderOptions
{
    /// <summary>Library-wide defaults.</summary>
    public static VideoDownloaderOptions Default { get; } = new();

    /// <summary>
    /// Camera back-pressure window size. Larger values mean the camera ships
    /// more packets between acknowledgements — faster on a clean link, more
    /// likely to stall on a flaky one. The first failed download is retried
    /// with <see cref="FallbackWindowSize"/>.
    /// </summary>
    public int WindowSize { get; init; } = 256;

    /// <summary>Window size used when the first attempt times out mid-stream.</summary>
    public int FallbackWindowSize { get; init; } = 64;

    /// <summary>
    /// Number of parallel media sessions for one recording. The camera
    /// happily serves several concurrent <c>playback</c> sessions for
    /// non-overlapping time slices, which often doubles end-to-end speed on
    /// high-bandwidth links.
    /// </summary>
    /// <remarks>
    /// Set to <c>1</c> for the safe single-session behaviour. The downloader
    /// silently falls back to a single session for recordings shorter than
    /// 10 seconds — slicing is wasteful below that threshold.
    /// </remarks>
    public int ParallelSessions { get; init; } = 1;

    /// <summary>Stop a download attempt if the camera goes silent for this many milliseconds.</summary>
    public int StallTimeoutMilliseconds { get; init; } = 30_000;

    /// <summary>How many extra seconds beyond the nominal end time to keep reading.</summary>
    public int PaddingSeconds { get; init; } = 5;

    /// <summary>Number of times to retry a stalled download before giving up.</summary>
    public int MaxRetries { get; init; } = 1;

    /// <summary>
    /// Minimum recording length (seconds) required before time-sliced parallel
    /// downloading kicks in. Below this threshold the downloader uses a
    /// single session regardless of <see cref="ParallelSessions"/>.
    /// </summary>
    public int ParallelSlicingThresholdSeconds { get; init; } = 10;
}
