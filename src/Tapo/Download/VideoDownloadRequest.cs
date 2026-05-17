using System.Collections.Generic;

namespace Tapo.Download;

/// <summary>
/// Describes a single download request — typically built from a
/// <see cref="Recordings.Recording"/> instance.
/// </summary>
public sealed class VideoDownloadRequest
{
    /// <summary>Creates a new download request for the supplied recording window.</summary>
    public VideoDownloadRequest(long startTimeUnix, long endTimeUnix)
    {
        StartTimeUnix = startTimeUnix;
        EndTimeUnix = endTimeUnix;
    }

    /// <summary>Recording start time as a Unix timestamp in camera-local seconds.</summary>
    public long StartTimeUnix { get; }

    /// <summary>Recording end time as a Unix timestamp in camera-local seconds.</summary>
    public long EndTimeUnix { get; }

    /// <summary>Channels to request — defaults to <c>[0, 1]</c> (video + audio).</summary>
    public IReadOnlyList<int> Channels { get; init; } = new[] { 0, 1 };

    /// <summary>Event types filter — defaults to <c>[1, 2]</c>, which the firmware reads as "motion + scheduled".</summary>
    public IReadOnlyList<int> EventTypes { get; init; } = new[] { 1, 2 };

    /// <summary>Playback scale; <c>1/1</c> means real time and is required for downloads.</summary>
    public string Scale { get; init; } = "1/1";

    /// <summary>How many extra seconds beyond the nominal end time to keep reading.</summary>
    public int PaddingSeconds { get; init; } = 5;

    /// <summary>Window size override. Larger means faster but riskier — see <see cref="MediaStream.MediaSessionOptions.WindowSize"/>.</summary>
    public int? WindowSize { get; init; }

    /// <summary>Stop the download if the camera stays silent for this many milliseconds.</summary>
    public int StallTimeoutMilliseconds { get; init; } = 30_000;
}
