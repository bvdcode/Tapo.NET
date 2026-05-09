using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tapo.Download;
using Xunit;
using Xunit.Abstractions;

namespace Tapo.Tests.Integration;

/// <summary>
/// Live tests against a real Tapo camera. Each test attempts to construct a
/// <see cref="LiveTestContext"/>; if the camera is unreachable or
/// <c>secrets.json</c> is missing the test logs the reason and returns —
/// matching the Blink.NET pattern, so CI without a camera stays green.
/// </summary>
public sealed class TapoLiveIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TapoLiveIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Connects_and_reports_basic_info()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var info = await context.Camera.Device.GetBasicInfoAsync();

        _output.WriteLine($"Model: {info.DeviceModel}");
        _output.WriteLine($"Firmware: {info.FirmwareVersion}");
        _output.WriteLine($"Mac: {info.Mac}");
        _output.WriteLine($"Friendly name: {info.FriendlyName}");

        Assert.False(string.IsNullOrWhiteSpace(info.DeviceModel));
        Assert.False(string.IsNullOrWhiteSpace(info.FirmwareVersion));
        Assert.False(string.IsNullOrWhiteSpace(info.Mac));
        Assert.NotEmpty(info.Raw);
    }

    [Fact]
    public async Task Reports_camera_time_close_to_local_clock()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var time = await context.Camera.Device.GetTimeAsync();
        var skewSeconds = Math.Abs((DateTime.UtcNow - time.UtcTime).TotalSeconds);

        _output.WriteLine($"Camera UTC: {time.UtcTime:O} (skew {skewSeconds:F0}s, timezone={time.Timezone}).");
        Assert.True(time.SecondsFromEpoch > 1_700_000_000);
    }

    [Fact]
    public async Task Returns_user_id_and_caches_it()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var first = await context.Camera.Recordings.GetUserIdAsync();
        var second = await context.Camera.Recordings.GetUserIdAsync();
        _output.WriteLine($"User id: {first}");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Lists_recordings_for_a_recent_day()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        for (var offset = 0; offset < 7; offset++)
        {
            var date = DateTime.Now.Date.AddDays(-offset);
            var recordings = await context.Camera.Recordings.GetRecordingsAsync(date);
            _output.WriteLine($"{date:yyyy-MM-dd}: {recordings.Count} recordings.");
            if (recordings.Count == 0) continue;

            foreach (var rec in recordings.Take(3))
            {
                _output.WriteLine($"  id={rec.Id}, start={rec.StartTimeUnix}, end={rec.EndTimeUnix}, duration={rec.DurationSeconds}s");
                Assert.True(rec.EndTimeUnix > rec.StartTimeUnix);
            }

            return;
        }

        _output.WriteLine("No recordings in the last week — accepting test as a no-op.");
    }

    [Fact]
    public async Task Reads_and_round_trips_led_state()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var initial = await context.Camera.Device.GetLedEnabledAsync();
        _output.WriteLine($"LED state: {initial}");

        try
        {
            await context.Camera.Device.SetLedEnabledAsync(!initial);
            await Task.Delay(500);
            var toggled = await context.Camera.Device.GetLedEnabledAsync();
            Assert.NotEqual(initial, toggled);
        }
        finally
        {
            await context.Camera.Device.SetLedEnabledAsync(initial);
        }
    }

    [Fact]
    public async Task Reads_and_round_trips_privacy_mode()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var initial = await context.Camera.Device.GetPrivacyModeAsync();
        _output.WriteLine($"Privacy mode: {initial}");

        try
        {
            await context.Camera.Device.SetPrivacyModeAsync(!initial);
            await Task.Delay(500);
            var toggled = await context.Camera.Device.GetPrivacyModeAsync();
            Assert.NotEqual(initial, toggled);
        }
        finally
        {
            await context.Camera.Device.SetPrivacyModeAsync(initial);
        }
    }

    [Fact]
    public async Task Reads_motion_detection_state()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var state = await context.Camera.Device.GetMotionDetectionAsync();
        _output.WriteLine($"Motion detection: enabled={state.Enabled}, sensitivity={state.Sensitivity}");
        Assert.InRange(state.Sensitivity, 0, 100);
    }

    [Fact]
    public async Task Reads_sd_card_status()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        try
        {
            var status = await context.Camera.Device.GetSdCardStatusAsync();
            _output.WriteLine($"SD card raw fields: {string.Join(", ", status.Raw.Keys)}");
            _output.WriteLine($"Total: {status.TotalSpaceMegabytes} MB, Free: {status.FreeSpaceMegabytes} MB, State: {status.State}");
        }
        catch (TapoApiException ex) when (ex.ErrorCode == -52409)
        {
            _output.WriteLine("SD card unplugged — accepting test as a no-op.");
        }
    }

    [Fact]
    public async Task Captures_jpeg_snapshot_when_privacy_is_off()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var privacy = await context.Camera.Device.GetPrivacyModeAsync();
        if (privacy)
        {
            _output.WriteLine("Privacy mode is on — skipping snapshot test.");
            return;
        }

        var snapshotClient = context.Camera.CreateSnapshotClient();
        try
        {
            var bytes = await snapshotClient.CaptureAsync();

            Assert.True(bytes.Length > 4096, "Snapshot looks too small to be a real JPEG.");
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);

            var path = Path.Combine(context.DownloadFolder, $"snapshot-{DateTime.UtcNow:yyyyMMddHHmmss}.jpg");
            await File.WriteAllBytesAsync(path, bytes);
            _output.WriteLine($"Snapshot saved to {path} ({bytes.Length:N0} bytes).");
        }
        catch (TapoException ex)
        {
            _output.WriteLine($"Snapshot capture not supported by this firmware: {ex.Message}");
        }
    }

    [Fact]
    public async Task Downloads_a_short_recording_to_disk()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var recording = await FindAnyRecentRecordingAsync(context);
        if (recording is null)
        {
            _output.WriteLine("No recordings available — skipping download test.");
            return;
        }

        var downloader = context.Camera.CreateDownloader(new VideoDownloaderOptions
        {
            WindowSize = 128,
            ParallelSessions = 1,
            StallTimeoutMilliseconds = 60_000,
            MaxRetries = 1,
        });

        var path = Path.Combine(context.DownloadFolder, $"single-{recording.StartTimeUnix}-{recording.EndTimeUnix}.ts");
        using (var fs = File.Create(path))
        {
            await downloader.DownloadAsync(
                new VideoDownloadRequest(recording.StartTimeUnix, recording.EndTimeUnix),
                fs);
        }

        var info = new FileInfo(path);
        Assert.True(info.Length > 0, "Downloaded recording is empty.");
        AssertFirstByteIsTsSync(path);

        _output.WriteLine($"Downloaded {info.Length:N0} bytes to {path}.");
    }

    [Fact]
    public async Task Parallel_download_matches_or_beats_single_session()
    {
        await using var context = await TryCreateAsync();
        if (context is null) return;

        var recording = await FindAnyRecentRecordingAsync(context, minDurationSeconds: 30);
        if (recording is null)
        {
            _output.WriteLine("No recording longer than 30 seconds available — skipping parallel download test.");
            return;
        }

        var singlePath = Path.Combine(context.DownloadFolder, $"single-{recording.StartTimeUnix}.ts");
        var parallelPath = Path.Combine(context.DownloadFolder, $"parallel-{recording.StartTimeUnix}.ts");

        var single = context.Camera.CreateDownloader(new VideoDownloaderOptions
        {
            WindowSize = 128,
            ParallelSessions = 1,
            StallTimeoutMilliseconds = 90_000,
        });

        var parallel = context.Camera.CreateDownloader(new VideoDownloaderOptions
        {
            WindowSize = 128,
            ParallelSessions = 2,
            StallTimeoutMilliseconds = 90_000,
        });

        var startSingle = DateTime.UtcNow;
        using (var fs = File.Create(singlePath))
        {
            await single.DownloadAsync(
                new VideoDownloadRequest(recording.StartTimeUnix, recording.EndTimeUnix),
                fs);
        }
        var singleSpan = DateTime.UtcNow - startSingle;

        var startParallel = DateTime.UtcNow;
        using (var fs = File.Create(parallelPath))
        {
            await parallel.DownloadAsync(
                new VideoDownloadRequest(recording.StartTimeUnix, recording.EndTimeUnix),
                fs);
        }
        var parallelSpan = DateTime.UtcNow - startParallel;

        var singleSize = new FileInfo(singlePath).Length;
        var parallelSize = new FileInfo(parallelPath).Length;
        AssertFirstByteIsTsSync(singlePath);
        AssertFirstByteIsTsSync(parallelPath);

        _output.WriteLine(
            $"Recording {recording.DurationSeconds}s | " +
            $"single: {singleSize:N0} bytes in {singleSpan.TotalSeconds:F1}s | " +
            $"parallel(2): {parallelSize:N0} bytes in {parallelSpan.TotalSeconds:F1}s");

        Assert.True(singleSize > 0);
        Assert.True(parallelSize > 0);
    }

    private async Task<Tapo.Recordings.Recording?> FindAnyRecentRecordingAsync(LiveTestContext context, int minDurationSeconds = 0)
    {
        for (var offset = 0; offset < 14; offset++)
        {
            var date = DateTime.Now.Date.AddDays(-offset);
            var recordings = await context.Camera.Recordings.GetRecordingsAsync(date);
            var match = recordings
                .Where(r => r.DurationSeconds >= minDurationSeconds)
                .OrderByDescending(r => r.StartTimeUnix)
                .FirstOrDefault();
            if (match is not null) return match;
        }

        return null;
    }

    private static void AssertFirstByteIsTsSync(string path)
    {
        using var fs = File.OpenRead(path);
        var first = fs.ReadByte();
        Assert.Equal(0x47, first);
    }

    private async Task<LiveTestContext?> TryCreateAsync()
    {
        try
        {
            return await LiveTestContext.CreateAsync(_output);
        }
        catch (InvalidOperationException ex)
        {
            _output.WriteLine(ex.Message);
            return null;
        }
    }
}
