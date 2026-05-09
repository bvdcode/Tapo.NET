using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Tapo;
using Tapo.Download;

namespace Tapo.Samples;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var secrets = TestSecrets.Load();
        if (secrets is null)
        {
            Console.Error.WriteLine("Create a secrets.json next to the executable. See README for the format.");
            return 2;
        }

        var date = args.Length > 0 ? args[0] : DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var outputDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "Tapo.NET.Downloads");

        Directory.CreateDirectory(outputDir);

        await using var camera = new TapoCamera(new TapoCameraOptions
        {
            Host = secrets.Host,
            Username = secrets.Username,
            Password = secrets.Password,
            CloudPassword = secrets.CloudPassword,
            DefaultWindowSize = secrets.WindowSize ?? 256,
        });

        Console.WriteLine($"Connecting to {secrets.Host}...");
        await camera.ConnectAsync();
        Console.WriteLine($"Logged in. Encryption: {camera.EncryptionMethod}.");

        var basicInfo = await camera.Device.GetBasicInfoAsync();
        Console.WriteLine($"Camera: {basicInfo.FriendlyName} ({basicInfo.DeviceModel}, fw {basicInfo.FirmwareVersion}).");

        var parsedDate = DateTime.ParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture);
        var recordings = await camera.Recordings.GetRecordingsAsync(parsedDate);
        Console.WriteLine($"Found {recordings.Count} recordings on {date}.");

        if (recordings.Count == 0)
        {
            return 0;
        }

        var downloader = camera.CreateDownloader(new VideoDownloaderOptions
        {
            WindowSize = secrets.WindowSize ?? 256,
            ParallelSessions = secrets.ParallelSessions ?? 2,
        });

        foreach (var recording in recordings)
        {
            var fileName = Path.Combine(outputDir, $"{recording.StartTimeUnix}-{recording.EndTimeUnix}.ts");
            Console.WriteLine($"Downloading {fileName} ({recording.DurationSeconds}s)...");

            using var fileStream = File.Create(fileName);
            var progress = new Progress<VideoDownloadProgress>(snapshot =>
            {
                Console.Write($"\r  packets={snapshot.PacketsReceived,6} bytes={snapshot.BytesWritten,12:N0}");
            });

            await downloader.DownloadAsync(
                new VideoDownloadRequest(recording.StartTimeUnix, recording.EndTimeUnix),
                fileStream,
                progress);
            Console.WriteLine();
        }

        Console.WriteLine($"Done. Output: {outputDir}");
        return 0;
    }

    private sealed class TestSecrets
    {
        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = "admin";

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("cloudPassword")]
        public string CloudPassword { get; set; } = string.Empty;

        [JsonPropertyName("windowSize")]
        public int? WindowSize { get; set; }

        [JsonPropertyName("parallelSessions")]
        public int? ParallelSessions { get; set; }

        public static TestSecrets? Load()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "secrets.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "secrets.json"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "secrets.json")),
            };

            var path = candidates.FirstOrDefault(File.Exists);
            if (path is null) return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TestSecrets>(json);
        }
    }
}
