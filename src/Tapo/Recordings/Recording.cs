namespace Tapo.Recordings;

/// <summary>
/// A single video segment available in the camera's SD-card archive.
/// </summary>
public sealed class Recording
{
    public Recording(int id, long startTimeUnix, long endTimeUnix)
    {
        Id = id;
        StartTimeUnix = startTimeUnix;
        EndTimeUnix = endTimeUnix;
    }

    /// <summary>Recording id as reported by the camera.</summary>
    public int Id { get; }

    /// <summary>Recording start time as a Unix timestamp in camera-local seconds.</summary>
    public long StartTimeUnix { get; }

    /// <summary>Recording end time as a Unix timestamp in camera-local seconds.</summary>
    public long EndTimeUnix { get; }

    /// <summary>Convenience: recording duration in seconds.</summary>
    public long DurationSeconds => EndTimeUnix - StartTimeUnix;
}
