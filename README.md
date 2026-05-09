![License](https://img.shields.io/github/license/bvdcode/Tapo.NET)
[![NuGet](https://img.shields.io/nuget/dt/Tapo.NET?color=%239100ff)](https://www.nuget.org/packages/Tapo.NET/)
[![NuGet version](https://img.shields.io/nuget/v/Tapo.NET.svg?label=nuget)](https://www.nuget.org/packages/Tapo.NET/)
[![FuGet](https://img.shields.io/badge/fuget-f88445)](https://www.fuget.org/packages/Tapo.NET)
[![Build](https://img.shields.io/github/actions/workflow/status/bvdcode/Tapo.NET/.github%2Fworkflows%2Fpublish-release.yml?label=build)](https://github.com/bvdcode/Tapo.NET/actions)
[![CodeFactor](https://www.codefactor.io/repository/github/bvdcode/Tapo.NET/badge)](https://www.codefactor.io/repository/github/bvdcode/Tapo.NET)
![Repo size](https://img.shields.io/github/repo-size/bvdcode/Tapo.NET)


# Tapo.NET

A .NET library (`netstandard2.1`) for TP-Link Tapo IP cameras. Speaks the
camera's local control channel and media-stream protocol directly — no
ffmpeg in the hot path, no cloud round-trip — so you can list and pull SD
recordings as fast as the LAN allows. Tested against the **TAPO C260** but
the protocol is shared across the IPC line (C100/C110/C200/C210/C220/C260
…). Runs on .NET 6/7/8/9, .NET Core 3.0+, Mono and any other runtime that
implements .NET Standard 2.1.

## Features

- **Native fast video download** — pulls SD recordings as raw MPEG-TS straight
  to disk over the camera's `8800/tcp` media stream. The camera's
  `X-Data-Window-Size` back-pressure knob is pumped directly and byte-aligned
  TS packets are written without any transcoding, so a 1-minute clip lands
  in seconds, not in real time.
- **Optional time-sliced parallel downloads** — split one recording across N
  concurrent media sessions and let the camera saturate the link.
- **Local login** — both the legacy `hashed=true` flow and the modern
  `encrypt_type=3` (`securePassthrough`) handshake. MD5/SHA-256 password
  digest is auto-detected.
- **Device commands** — basic info, time, LED, lens-mask (privacy mode),
  motion detection, SD-card status, reboot — and an escape hatch (`Methods`)
  for any Tapo API method the typed clients don't cover.
- **Snapshots** — pull a single JPEG keyframe over the media stream without
  spinning up RTSP.
- **Pluggable architecture** — every layer (`IControlChannel`, `IMediaSession`,
  `IVideoDownloader`, `IRecordingsClient`, `IDeviceClient`, `ISnapshotClient`)
  sits behind an interface, so callers can swap in fakes for tests.

## Installation

Via .NET CLI:

```powershell
dotnet add package Tapo.NET
```

Via PackageReference:

```xml
<ItemGroup>
  <PackageReference Include="Tapo.NET" Version="x.y.z" />
  <!-- see the latest version on https://www.nuget.org/packages/Tapo.NET/ -->
</ItemGroup>
```

## Quick start

Login to the camera, list yesterday's recordings, and download all of them
as MPEG-TS files:

```csharp
using Tapo;
using Tapo.Download;

await using var camera = new TapoCamera(new TapoCameraOptions
{
    Host = "192.168.1.50",
    Username = "admin",
    Password = "your-camera-password",
    CloudPassword = "your-camera-password", // usually the same value
    DefaultWindowSize = 256,
});

await camera.ConnectAsync();

var info = await camera.Device.GetBasicInfoAsync();
Console.WriteLine($"Connected to {info.FriendlyName} ({info.DeviceModel}), fw {info.FirmwareVersion}");

var date = DateTime.Today.AddDays(-1);
var recordings = await camera.Recordings.GetRecordingsAsync(date);

var downloader = camera.CreateDownloader(new VideoDownloaderOptions
{
    WindowSize       = 256,
    ParallelSessions = 2,    // try 1 if the link is flaky
});

foreach (var rec in recordings)
{
    using var fs = File.Create($"{rec.StartTimeUnix}-{rec.EndTimeUnix}.ts");
    await downloader.DownloadAsync(
        new VideoDownloadRequest(rec.StartTimeUnix, rec.EndTimeUnix),
        fs);
}
```

The downloader writes raw MPEG-TS. Wrap it in MP4 once with
`ffmpeg -i input.ts -c copy output.mp4` if you need a more portable
container — the copy is essentially instantaneous because no transcoding is
involved.

### Snapshot

```csharp
var snapshotClient = camera.CreateSnapshotClient();
var jpeg = await snapshotClient.CaptureAsync();
await File.WriteAllBytesAsync("now.jpg", jpeg);
```

### Device commands

```csharp
// Toggle the front status LED
await camera.Device.SetLedEnabledAsync(false);

// Lens mask (privacy mode)
var privacyOn = await camera.Device.GetPrivacyModeAsync();
await camera.Device.SetPrivacyModeAsync(true);

// Motion detection — read state, raise sensitivity to 80
var motion = await camera.Device.GetMotionDetectionAsync();
await camera.Device.SetMotionDetectionAsync(enabled: true, sensitivity: 80);

// SD card health and free space
var sd = await camera.Device.GetSdCardStatusAsync();
Console.WriteLine($"SD: {sd.FreeSpaceMegabytes}/{sd.TotalSpaceMegabytes} MB free");

// Reboot
await camera.Device.RebootAsync();
```

### Calling raw Tapo methods

The typed `Device` client covers the common surface; for anything else, use
the underlying invoker directly:

```csharp
using var doc = await camera.Methods.InvokeAsync(
    "getOsd",
    new { OSD = new { name = new[] { "logo", "date", "week", "font" } } });

Console.WriteLine(doc.RootElement);
```

## API surface

Top-level facade:

- `Task ConnectAsync(...)` — performs login.
- `IRecordingsClient Recordings` — recording listing/search.
- `IDeviceClient Device` — basic info, time, LED, privacy, motion detection,
  SD card, reboot.
- `IVideoDownloader CreateDownloader(...)` — fast SD downloads.
- `ISnapshotClient CreateSnapshotClient()` — JPEG capture.
- `TapoMethodInvoker Methods` — generic Tapo API calls.
- `IControlChannel ControlChannel` — diagnostic access to the wire layer.

Throughput knobs live on `VideoDownloaderOptions`:

| Property                          | Default | What it does                                                                |
|-----------------------------------|---------|-----------------------------------------------------------------------------|
| `WindowSize`                      | 256     | Camera back-pressure window. Larger = faster, riskier on flaky links.       |
| `FallbackWindowSize`              | 64      | Window used after a stall.                                                  |
| `ParallelSessions`                | 1       | Number of concurrent time-sliced sessions per recording.                    |
| `ParallelSlicingThresholdSeconds` | 10      | Below this clip length the downloader stays single-session regardless.      |
| `StallTimeoutMilliseconds`        | 30 000  | How long to wait before treating the camera as silent.                      |
| `MaxRetries`                      | 1       | Number of retries before raising `TapoDownloadStalledException`.            |
| `PaddingSeconds`                  | 5       | Extra seconds beyond the nominal end time to keep reading.                  |

## Sample console application

A small example lives in [`src/Tapo.ConsoleTest`](src/Tapo.ConsoleTest):

1. Create a `secrets.json` next to `Program.cs` (gitignored):

   ```json
   {
     "host": "192.168.1.50",
     "username": "admin",
     "password": "your-camera-password",
     "cloudPassword": "your-camera-password",
     "controlPort": 443,
     "streamPort": 8800,
     "windowSize": 256,
     "parallelSessions": 2
   }
   ```

2. Build and run:

   ```powershell
   cd src/Tapo.ConsoleTest
   dotnet run -- 20251109     # download recordings of 2025-11-09
   ```

   With no arguments the sample downloads recordings of the current day into
   `%TEMP%/Tapo.NET.Downloads`.

## Live integration tests

The repository includes live integration tests in
[`src/Tapo.Tests`](src/Tapo.Tests). They exercise:

- Local login (legacy `hashed=true` and modern `encrypt_type=3`).
- `getDeviceInfo`, `getClockStatus`, recordings listing.
- LED on/off and lens-mask toggle (with state restoration).
- Motion-detection state read.
- `getSdCardStatus` (gracefully handles the "no SD card" error).
- JPEG snapshot capture (when privacy mode is off).
- Single-session and time-sliced parallel downloads (compares wall-clock
  duration of both modes for a recording of 30 s+).

Tests load `secrets.json` from the test output directory. Drop a copy
next to `src/Tapo.Tests/Tapo.Tests.csproj` (or reuse the one next to the
console-test project) and the test csproj will copy it to the test output
on build. Tests skip cleanly with a console note when the file is missing
or the camera is not reachable, so CI without a camera stays green.

```powershell
dotnet test src/Tapo.Tests/Tapo.Tests.csproj
```

Downloaded artefacts go to `%TEMP%/Tapo.NET.Tests.Downloads`.

## Repository layout

```
Tapo.NET/
├── README.md
├── LICENSE
├── GitVersion.yml
├── Directory.Build.props
└── src/
    ├── Tapo.sln
    ├── Tapo/                       library (PackageId: Tapo.NET, RootNamespace: Tapo)
    │   └── Tapo.csproj
    ├── Tapo.ConsoleTest/           sample CLI + secrets.json template
    │   └── Tapo.ConsoleTest.csproj
    └── Tapo.Tests/                 xUnit unit + live integration tests
        └── Tapo.Tests.csproj
```

## Disclaimer

This project is not affiliated with TP-Link Technologies, the Tapo brand, or
any other companies. Use at your own risk and in accordance with TP-Link's
terms of service.

## License

MIT — see [LICENSE](LICENSE).
