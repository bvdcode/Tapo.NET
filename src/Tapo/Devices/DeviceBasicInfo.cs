using System.Collections.Generic;
using System.Text.Json;

namespace Tapo.Devices;

/// <summary>
/// Subset of <c>getDeviceInfo / device_info.basic_info</c> the library exposes
/// directly. Camera firmwares add fields between releases — anything not
/// modeled here is preserved verbatim in <see cref="Raw"/>.
/// </summary>
public sealed class DeviceBasicInfo
{
    /// <summary>Creates a new <see cref="DeviceBasicInfo"/>.</summary>
    public DeviceBasicInfo(
        string? deviceModel,
        string? deviceType,
        string? deviceName,
        string? deviceAlias,
        string? hardwareVersion,
        string? firmwareVersion,
        string? mac,
        string? deviceId,
        string? friendlyName,
        IReadOnlyDictionary<string, JsonElement> raw)
    {
        DeviceModel = deviceModel;
        DeviceType = deviceType;
        DeviceName = deviceName;
        DeviceAlias = deviceAlias;
        HardwareVersion = hardwareVersion;
        FirmwareVersion = firmwareVersion;
        Mac = mac;
        DeviceId = deviceId;
        FriendlyName = friendlyName;
        Raw = raw;
    }

    /// <summary>Camera model — e.g. <c>C260</c>.</summary>
    public string? DeviceModel { get; }

    /// <summary>Internal device type — e.g. <c>SMART.IPCAMERA</c>.</summary>
    public string? DeviceType { get; }

    /// <summary>Device internal name (e.g. <c>C260 1.0</c>).</summary>
    public string? DeviceName { get; }

    /// <summary>User-assigned alias from the Tapo app (Base64-encoded by the camera; decoded on read).</summary>
    public string? DeviceAlias { get; }

    /// <summary>Hardware revision string.</summary>
    public string? HardwareVersion { get; }

    /// <summary>Firmware version string.</summary>
    public string? FirmwareVersion { get; }

    /// <summary>MAC address (camera-formatted, dash-separated).</summary>
    public string? Mac { get; }

    /// <summary>Stable device id reported by the camera.</summary>
    public string? DeviceId { get; }

    /// <summary>Friendly name suitable for UIs — falls back to <see cref="DeviceModel"/> when no alias is set.</summary>
    public string? FriendlyName { get; }

    /// <summary>Untouched <c>basic_info</c> object so callers can read fields the library does not model.</summary>
    public IReadOnlyDictionary<string, JsonElement> Raw { get; }
}
