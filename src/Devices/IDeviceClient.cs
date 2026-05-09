using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.Devices;

/// <summary>
/// System-level commands the camera understands: identification, time, LED,
/// privacy mask, motion detection, SD-card status and reboot. Covers the
/// most commonly used parts of the Tapo API <c>get*/set*</c> surface.
/// </summary>
public interface IDeviceClient
{
    /// <summary>Returns the camera's basic info (model, fw, mac, alias, …).</summary>
    Task<DeviceBasicInfo> GetBasicInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the camera's current local time.</summary>
    Task<DeviceTime> GetTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>Toggles the front status LED.</summary>
    Task SetLedEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>True when the front status LED is currently on.</summary>
    Task<bool> GetLedEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the lens-mask (privacy) state.</summary>
    Task<bool> GetPrivacyModeAsync(CancellationToken cancellationToken = default);

    /// <summary>Enables or disables the lens mask. Disables live view until cleared.</summary>
    Task SetPrivacyModeAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Returns motion detection state and digital sensitivity (0 – 100).</summary>
    Task<MotionDetectionState> GetMotionDetectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates motion detection state and (optionally) digital sensitivity.</summary>
    Task SetMotionDetectionAsync(bool enabled, int? sensitivity = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the SD card status (capacity, free space, health, recording state).</summary>
    Task<SdCardStatus> GetSdCardStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Reboots the camera.</summary>
    Task RebootAsync(CancellationToken cancellationToken = default);
}
