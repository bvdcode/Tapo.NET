namespace Tapo.Download;

/// <summary>
/// Lightweight progress snapshot reported through <see cref="System.IProgress{T}"/>.
/// </summary>
public readonly struct VideoDownloadProgress
{
    public VideoDownloadProgress(long bytesWritten, long packetsReceived, long? totalSeconds)
    {
        BytesWritten = bytesWritten;
        PacketsReceived = packetsReceived;
        TotalSeconds = totalSeconds;
    }

    /// <summary>Bytes written to the destination stream so far.</summary>
    public long BytesWritten { get; }

    /// <summary>Number of multipart parts received from the camera.</summary>
    public long PacketsReceived { get; }

    /// <summary>Nominal length of the recording in seconds — useful for percent calculations.</summary>
    public long? TotalSeconds { get; }
}
