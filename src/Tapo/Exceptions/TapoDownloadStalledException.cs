namespace Tapo;

/// <summary>
/// Raised when a video-download attempt finishes without receiving any
/// MPEG-TS data — typically because the camera silently dropped the
/// session under load. The downloader catches this internally and retries
/// with a smaller window before propagating it to the caller.
/// </summary>
public sealed class TapoDownloadStalledException : TapoException
{
    /// <summary>Creates a stall exception for the supplied time slice.</summary>
    public TapoDownloadStalledException(long startUnix, long endUnix)
        : base($"Camera went silent during a recording download for [{startUnix}, {endUnix}] without sending any MPEG-TS data.")
    {
        StartUnix = startUnix;
        EndUnix = endUnix;
    }

    /// <summary>Slice start time (Unix seconds, camera local).</summary>
    public long StartUnix { get; }

    /// <summary>Slice end time (Unix seconds, camera local).</summary>
    public long EndUnix { get; }
}
