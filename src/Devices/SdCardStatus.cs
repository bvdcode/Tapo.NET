using System.Collections.Generic;
using System.Text.Json;

namespace Tapo.Devices;

/// <summary>SD card state reported by <c>getSdCardStatus</c>.</summary>
public sealed class SdCardStatus
{
    /// <summary>Creates an SD card status snapshot.</summary>
    public SdCardStatus(
        long? totalSpaceMegabytes,
        long? freeSpaceMegabytes,
        bool? isRecording,
        string? state,
        IReadOnlyDictionary<string, JsonElement> raw)
    {
        TotalSpaceMegabytes = totalSpaceMegabytes;
        FreeSpaceMegabytes = freeSpaceMegabytes;
        IsRecording = isRecording;
        State = state;
        Raw = raw;
    }

    /// <summary>Total card capacity in megabytes, or <see langword="null"/> if not reported.</summary>
    public long? TotalSpaceMegabytes { get; }

    /// <summary>Free card space in megabytes, or <see langword="null"/> if not reported.</summary>
    public long? FreeSpaceMegabytes { get; }

    /// <summary>True when the camera is currently writing to the card.</summary>
    public bool? IsRecording { get; }

    /// <summary>Card state string — typical values <c>normal</c>, <c>unformatted</c>, <c>none</c>.</summary>
    public string? State { get; }

    /// <summary>Untouched <c>hd_info</c> entry for callers that need extra fields.</summary>
    public IReadOnlyDictionary<string, JsonElement> Raw { get; }
}
