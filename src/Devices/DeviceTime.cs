using System;

namespace Tapo.Devices;

/// <summary>
/// Camera-local clock snapshot. The Tapo firmware reports an integer Unix
/// timestamp plus a textual timezone name; this DTO surfaces both.
/// </summary>
public sealed class DeviceTime
{
    /// <summary>Creates a clock snapshot.</summary>
    public DeviceTime(long secondsFromEpoch, string? timezone)
    {
        SecondsFromEpoch = secondsFromEpoch;
        Timezone = timezone;
        UtcTime = DateTimeOffset.FromUnixTimeSeconds(secondsFromEpoch).UtcDateTime;
    }

    /// <summary>Camera-local time as a Unix epoch second count.</summary>
    public long SecondsFromEpoch { get; }

    /// <summary>Camera-reported timezone string (POSIX style), or <see langword="null"/> when unavailable.</summary>
    public string? Timezone { get; }

    /// <summary>Convenience: <see cref="SecondsFromEpoch"/> turned into a <see cref="DateTime"/> in UTC.</summary>
    public DateTime UtcTime { get; }
}
