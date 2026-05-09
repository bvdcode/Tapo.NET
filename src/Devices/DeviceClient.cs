using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Control;
using Tapo.Internal;

namespace Tapo.Devices;

/// <summary>
/// Default <see cref="IDeviceClient"/>. Talks to the camera through a
/// <see cref="TapoMethodInvoker"/> and translates raw JSON into the typed
/// DTOs documented on the interface.
/// </summary>
public sealed class DeviceClient : IDeviceClient
{
    private readonly TapoMethodInvoker _invoker;

    /// <summary>Creates a device client.</summary>
    public DeviceClient(TapoMethodInvoker invoker)
    {
        Throw.IfNull(invoker);
        _invoker = invoker;
    }

    /// <inheritdoc />
    public async Task<DeviceBasicInfo> GetBasicInfoAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _invoker.InvokeAsync(
            "getDeviceInfo",
            new { device_info = new { name = new[] { "basic_info" } } },
            cancellationToken).ConfigureAwait(false);

        var basicInfo = doc.RootElement
            .GetPropertyOrThrow("device_info")
            .GetPropertyOrThrow("basic_info");

        var raw = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in basicInfo.EnumerateObject())
        {
            raw[prop.Name] = prop.Value.Clone();
        }

        return new DeviceBasicInfo(
            deviceModel: GetString(basicInfo, "device_model"),
            deviceType: GetString(basicInfo, "device_type"),
            deviceName: GetString(basicInfo, "device_name"),
            deviceAlias: TryDecodeBase64(GetString(basicInfo, "device_alias")),
            hardwareVersion: GetString(basicInfo, "hw_version") ?? GetString(basicInfo, "hardware_version"),
            firmwareVersion: GetString(basicInfo, "sw_version") ?? GetString(basicInfo, "firmware_version"),
            mac: GetString(basicInfo, "mac"),
            deviceId: GetString(basicInfo, "dev_id") ?? GetString(basicInfo, "device_id"),
            friendlyName: TryDecodeBase64(GetString(basicInfo, "friendly_name")) ?? TryDecodeBase64(GetString(basicInfo, "device_alias")) ?? GetString(basicInfo, "device_model"),
            raw: raw);
    }

    /// <inheritdoc />
    public async Task<DeviceTime> GetTimeAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _invoker.InvokeAsync(
            "getClockStatus",
            new { system = new { name = "clock_status" } },
            cancellationToken).ConfigureAwait(false);

        var clock = doc.RootElement
            .GetPropertyOrThrow("system")
            .GetPropertyOrThrow("clock_status");

        var seconds = clock.GetPropertyOrThrow("seconds_from_1970").GetInt64();
        string? timezone = null;
        if (clock.TryGetProperty("local_time", out var localTime) && localTime.ValueKind == JsonValueKind.String)
        {
            timezone = localTime.GetString();
        }

        return new DeviceTime(seconds, timezone);
    }

    /// <inheritdoc />
    public Task SetLedEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        => _invoker.ExecuteAsync(
            "setLedStatus",
            new { led = new { config = new { enabled = enabled ? "on" : "off" } } },
            cancellationToken);

    /// <inheritdoc />
    public async Task<bool> GetLedEnabledAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _invoker.InvokeAsync(
            "getLedStatus",
            new { led = new { name = new[] { "config" } } },
            cancellationToken).ConfigureAwait(false);

        var enabled = doc.RootElement
            .GetPropertyOrThrow("led")
            .GetPropertyOrThrow("config")
            .GetPropertyOrThrow("enabled")
            .GetString();

        return string.Equals(enabled, "on", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<bool> GetPrivacyModeAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _invoker.InvokeAsync(
            "getLensMaskConfig",
            new { lens_mask = new { name = new[] { "lens_mask_info" } } },
            cancellationToken).ConfigureAwait(false);

        var enabled = doc.RootElement
            .GetPropertyOrThrow("lens_mask")
            .GetPropertyOrThrow("lens_mask_info")
            .GetPropertyOrThrow("enabled")
            .GetString();

        return string.Equals(enabled, "on", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task SetPrivacyModeAsync(bool enabled, CancellationToken cancellationToken = default)
        => _invoker.ExecuteAsync(
            "setLensMaskConfig",
            new { lens_mask = new { lens_mask_info = new { enabled = enabled ? "on" : "off" } } },
            cancellationToken);

    /// <inheritdoc />
    public async Task<MotionDetectionState> GetMotionDetectionAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _invoker.InvokeAsync(
            "getDetectionConfig",
            new { motion_detection = new { name = new[] { "motion_det" } } },
            cancellationToken).ConfigureAwait(false);

        var motion = doc.RootElement
            .GetPropertyOrThrow("motion_detection")
            .GetPropertyOrThrow("motion_det");

        var enabled = motion.GetPropertyOrThrow("enabled").GetString();
        int sensitivity = 0;
        if (motion.TryGetProperty("digital_sensitivity", out var sensElem))
        {
            if (sensElem.ValueKind == JsonValueKind.Number && sensElem.TryGetInt32(out var num))
            {
                sensitivity = num;
            }
            else if (sensElem.ValueKind == JsonValueKind.String &&
                int.TryParse(sensElem.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                sensitivity = parsed;
            }
        }

        return new MotionDetectionState(string.Equals(enabled, "on", StringComparison.OrdinalIgnoreCase), sensitivity);
    }

    /// <inheritdoc />
    public Task SetMotionDetectionAsync(bool enabled, int? sensitivity = null, CancellationToken cancellationToken = default)
    {
        if (sensitivity is { } s && (s < 0 || s > 100))
        {
            throw new ArgumentOutOfRangeException(nameof(sensitivity), s, "Motion detection sensitivity must be between 0 and 100.");
        }

        var payload = sensitivity is null
            ? (object)new
            {
                motion_detection = new
                {
                    motion_det = new { enabled = enabled ? "on" : "off" },
                },
            }
            : new
            {
                motion_detection = new
                {
                    motion_det = new
                    {
                        enabled = enabled ? "on" : "off",
                        digital_sensitivity = sensitivity.Value.ToString(CultureInfo.InvariantCulture),
                    },
                },
            };

        return _invoker.ExecuteAsync("setDetectionConfig", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SdCardStatus> GetSdCardStatusAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await _invoker.InvokeAsync(
            "getSdCardStatus",
            new { harddisk_manage = new { table = new[] { "hd_info" } } },
            cancellationToken).ConfigureAwait(false);

        var harddisk = doc.RootElement.GetPropertyOrThrow("harddisk_manage");
        if (!harddisk.TryGetProperty("hd_info", out var hdInfoElem))
        {
            return new SdCardStatus(null, null, null, null, new Dictionary<string, JsonElement>());
        }

        // The camera answers with a single-element array of {hd_info: {...}}.
        JsonElement entry = hdInfoElem;
        if (hdInfoElem.ValueKind == JsonValueKind.Array && hdInfoElem.GetArrayLength() > 0)
        {
            entry = hdInfoElem[0];
        }

        if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("hd_info", out var nested))
        {
            entry = nested;
        }

        var raw = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in entry.EnumerateObject())
        {
            raw[prop.Name] = prop.Value.Clone();
        }

        return new SdCardStatus(
            totalSpaceMegabytes: TryGetLong(entry, "totalSpace") ?? TryGetLong(entry, "total_space"),
            freeSpaceMegabytes: TryGetLong(entry, "freeSpace") ?? TryGetLong(entry, "free_space"),
            isRecording: TryGetBool(entry, "rw_status") ?? TryGetBool(entry, "is_recording"),
            state: GetString(entry, "disk_name") ?? GetString(entry, "status"),
            raw: raw);
    }

    /// <inheritdoc />
    public Task RebootAsync(CancellationToken cancellationToken = default)
        => _invoker.ExecuteAsync(
            "rebootSystem",
            new { system = new { reboot = "null" } },
            cancellationToken);

    private static string? GetString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static long? TryGetLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v)) return v;
        if (prop.ValueKind == JsonValueKind.String &&
            long.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? TryGetBool(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(prop.GetString(), "on", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(prop.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number when prop.TryGetInt32(out var n) => n != 0,
            _ => null,
        };
    }

    private static string? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        try
        {
            var decoded = Convert.FromBase64String(value);
            var text = Encoding.UTF8.GetString(decoded);

            // The camera always Base64-encodes alphanumeric aliases — bail out
            // if the decoded string looks like garbage so we don't replace a
            // perfectly valid plain-text alias with mojibake.
            return text.Length > 0 && IsLikelyText(text) ? text : value;
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static bool IsLikelyText(string text)
    {
        foreach (var c in text)
        {
            if (c >= 0x20) continue;
            if (c == '\r' || c == '\n' || c == '\t') continue;
            return false;
        }

        return true;
    }
}
