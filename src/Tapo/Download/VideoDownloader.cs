using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tapo.Internal;
using Tapo.MediaStream;
using Tapo.Recordings;

namespace Tapo.Download;

/// <summary>
/// High-throughput video downloader. The implementation owns the
/// <see cref="IMediaSession"/> for the duration of a single download, sends a
/// <c>playback</c> request and copies every <c>video/mp2t</c> part it
/// receives into the supplied destination — sync-byte aligned to keep the
/// resulting <c>.ts</c> file valid for tools like ffmpeg.
/// </summary>
/// <remarks>
/// <para>Two operating modes are available:</para>
/// <list type="bullet">
///   <item><description>
///     Single session (default) — issues one <c>playback</c> request and pumps
///     the response at the configured <see cref="VideoDownloaderOptions.WindowSize"/>.
///     No ffmpeg in the hot path: the camera's MPEG-TS bytes go straight to disk.
///   </description></item>
///   <item><description>
///     Time-sliced parallel sessions — splits a recording into N
///     non-overlapping intervals and downloads them concurrently into a
///     temporary file each, then concatenates. Triggered by
///     <see cref="VideoDownloaderOptions.ParallelSessions"/> &gt; 1.
///   </description></item>
/// </list>
/// </remarks>
public sealed class VideoDownloader : IVideoDownloader
{
    private const int TsPacketSize = 188;
    private const byte TsSyncByte = 0x47;

    private readonly Func<MediaSessionOptions, IMediaSession> _sessionFactory;
    private readonly MediaSessionOptions _baseOptions;
    private readonly IRecordingsClient _recordingsClient;
    private readonly VideoDownloaderOptions _downloaderOptions;

    /// <summary>Creates a video downloader.</summary>
    /// <param name="baseOptions">Media-session template — host, port, encryption, default window size.</param>
    /// <param name="recordingsClient">Used to look up the camera's stable user id before each download.</param>
    /// <param name="downloaderOptions">Throughput / retry tunables. Defaults to <see cref="VideoDownloaderOptions.Default"/>.</param>
    /// <param name="sessionFactory">Override the session factory (tests).</param>
    public VideoDownloader(
        MediaSessionOptions baseOptions,
        IRecordingsClient recordingsClient,
        VideoDownloaderOptions? downloaderOptions = null,
        Func<MediaSessionOptions, IMediaSession>? sessionFactory = null)
    {
        Throw.IfNull(baseOptions);
        Throw.IfNull(recordingsClient);

        _baseOptions = baseOptions;
        _recordingsClient = recordingsClient;
        _downloaderOptions = downloaderOptions ?? VideoDownloaderOptions.Default;
        _sessionFactory = sessionFactory ?? (opts => new MediaSession(opts));
    }

    /// <inheritdoc />
    public async Task DownloadAsync(
        VideoDownloadRequest request,
        Stream destination,
        IProgress<VideoDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        Throw.IfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Destination must be writable.", nameof(destination));

        var userId = await _recordingsClient.GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var totalSeconds = Math.Max(0, request.EndTimeUnix - request.StartTimeUnix + EffectivePadding(request));
        var slices = BuildSlices(request);

        if (slices.Count == 1)
        {
            await DownloadSliceWithRetryAsync(
                slices[0],
                destination,
                userId,
                request,
                new ProgressAggregator(progress, totalSeconds),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await DownloadInParallelAsync(
            slices,
            destination,
            userId,
            totalSeconds,
            request,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadInParallelAsync(
        IReadOnlyList<TimeSlice> slices,
        Stream destination,
        int userId,
        long totalSeconds,
        VideoDownloadRequest request,
        IProgress<VideoDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var aggregate = new ProgressAggregator(progress, totalSeconds);
        var tempFiles = new string[slices.Count];

        try
        {
            var tasks = new Task[slices.Count];
            for (var i = 0; i < slices.Count; i++)
            {
                var index = i;
                tempFiles[index] = Path.Combine(
                    Path.GetTempPath(),
                    $"tapo-net-{Guid.NewGuid():N}.ts");

                tasks[index] = Task.Run(async () =>
                {
                    using var fs = new FileStream(
                        tempFiles[index],
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        useAsync: true);

                    await DownloadSliceWithRetryAsync(
                        slices[index],
                        fs,
                        userId,
                        request,
                        aggregate,
                        cancellationToken).ConfigureAwait(false);
                }, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var path in tempFiles)
            {
                using var fs = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                await fs.CopyToAsync(destination, 64 * 1024, cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var path in tempFiles)
            {
                if (path is null) continue;
                try { File.Delete(path); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private async Task DownloadSliceWithRetryAsync(
        TimeSlice slice,
        Stream destination,
        int userId,
        VideoDownloadRequest request,
        ProgressAggregator aggregate,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            var window = attempt == 0
                ? request.WindowSize ?? _downloaderOptions.WindowSize
                : _downloaderOptions.FallbackWindowSize;

            try
            {
                await PumpSliceAsync(
                    slice,
                    destination,
                    userId,
                    window,
                    request,
                    aggregate,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (TapoDownloadStalledException) when (attempt < _downloaderOptions.MaxRetries)
            {
                attempt++;
            }
        }
    }

    private async Task PumpSliceAsync(
        TimeSlice slice,
        Stream destination,
        int userId,
        int window,
        VideoDownloadRequest request,
        ProgressAggregator aggregate,
        CancellationToken cancellationToken)
    {
        var sessionOptions = BuildSessionOptions(window);
        await using var session = _sessionFactory(sessionOptions);
        await session.StartAsync(cancellationToken).ConfigureAwait(false);

        var payload = BuildPlaybackPayload(slice, userId, request);
        var stallTimeout = _downloaderOptions.StallTimeoutMilliseconds;

        var carry = Array.Empty<byte>();
        var receivedAnyTs = false;
        var sawFinishedNotification = false;

        await foreach (var response in session.TransceiveAsync(
                           payload,
                           MimeTypes.Json,
                           noDataTimeoutMilliseconds: stallTimeout,
                           cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (string.Equals(response.MimeType, MimeTypes.MpegTs, StringComparison.OrdinalIgnoreCase))
                {
                    receivedAnyTs = true;

                    var (written, newCarry) = await WriteTsAlignedAsync(
                        destination,
                        carry,
                        response.Payload,
                        cancellationToken).ConfigureAwait(false);
                    carry = newCarry;
                    aggregate.Add(written, packets: 1);
                }
                else if (string.Equals(response.MimeType, MimeTypes.Json, StringComparison.OrdinalIgnoreCase) &&
                         IsFinishedNotification(response))
                {
                    sawFinishedNotification = true;
                    break;
                }
            }
            finally
            {
                response.JsonDocument?.Dispose();
            }
        }

        if (carry.Length >= TsPacketSize)
        {
            var aligned = AlignToPacketBoundary(carry);
            if (aligned.Length > 0)
            {
                await destination.WriteAsync(aligned, 0, aligned.Length, cancellationToken).ConfigureAwait(false);
                aggregate.Add(aligned.Length, packets: 0);
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (!receivedAnyTs && !sawFinishedNotification)
        {
            throw new TapoDownloadStalledException(slice.StartUnix, slice.EndUnix);
        }
    }

    private MediaSessionOptions BuildSessionOptions(int window) => new()
    {
        Host = _baseOptions.Host,
        Port = _baseOptions.Port,
        Username = _baseOptions.Username,
        CloudPassword = _baseOptions.CloudPassword,
        SuperSecretKey = _baseOptions.SuperSecretKey,
        EncryptionMethod = _baseOptions.EncryptionMethod,
        WindowSize = window,
        QueryParameters = _baseOptions.QueryParameters,
        ConnectTimeoutMilliseconds = _baseOptions.ConnectTimeoutMilliseconds,
        IoTimeoutMilliseconds = _baseOptions.IoTimeoutMilliseconds,
    };

    private IReadOnlyList<TimeSlice> BuildSlices(VideoDownloadRequest request)
    {
        var parallel = Math.Max(1, _downloaderOptions.ParallelSessions);
        var paddedEnd = request.EndTimeUnix + EffectivePadding(request);
        var totalSeconds = Math.Max(0, paddedEnd - request.StartTimeUnix);

        if (parallel <= 1 || totalSeconds < _downloaderOptions.ParallelSlicingThresholdSeconds)
        {
            return new[] { new TimeSlice(request.StartTimeUnix, paddedEnd) };
        }

        var slices = new List<TimeSlice>(parallel);
        var sliceLength = totalSeconds / parallel;
        var current = request.StartTimeUnix;

        for (var i = 0; i < parallel - 1; i++)
        {
            var sliceEnd = current + sliceLength;
            slices.Add(new TimeSlice(current, sliceEnd));
            current = sliceEnd;
        }

        slices.Add(new TimeSlice(current, paddedEnd));
        return slices;
    }

    private int EffectivePadding(VideoDownloadRequest request)
        => request.PaddingSeconds >= 0 ? request.PaddingSeconds : _downloaderOptions.PaddingSeconds;

    private static byte[] BuildPlaybackPayload(TimeSlice slice, int userId, VideoDownloadRequest request)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "request");
            writer.WriteNumber("seq", 1);
            writer.WriteStartObject("params");

            writer.WriteStartObject("playback");
            writer.WriteNumber("client_id", userId);
            writer.WritePropertyName("channels");
            writer.WriteStartArray();
            foreach (var c in request.Channels) writer.WriteNumberValue(c);
            writer.WriteEndArray();
            writer.WriteString("scale", request.Scale);
            writer.WriteString("start_time", slice.StartUnix.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("end_time", slice.EndUnix.ToString(CultureInfo.InvariantCulture));
            writer.WritePropertyName("event_type");
            writer.WriteStartArray();
            foreach (var e in request.EventTypes) writer.WriteNumberValue(e);
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteString("method", "get");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return ms.ToArray();
    }

    private static bool IsFinishedNotification(MediaResponse response)
    {
        if (response.JsonDocument is null) return false;
        var root = response.JsonDocument.RootElement;
        if (!root.TryGetProperty("type", out var type) ||
            !string.Equals(type.GetString(), "notification", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!root.TryGetProperty("params", out var paramsElem)) return false;
        if (!paramsElem.TryGetProperty("event_type", out var ev) ||
            !string.Equals(ev.GetString(), "stream_status", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return paramsElem.TryGetProperty("status", out var status) &&
               string.Equals(status.GetString(), "finished", StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask<(int Written, byte[] Carry)> WriteTsAlignedAsync(
        Stream destination,
        byte[] carry,
        byte[] chunk,
        CancellationToken cancellationToken)
    {
        var combined = carry.Length == 0
            ? chunk
            : ConcatenateBuffers(carry, chunk);

        var firstSync = IndexOfSync(combined, 0);
        if (firstSync < 0)
        {
            return (0, combined);
        }

        var packetsByteCount = ((combined.Length - firstSync) / TsPacketSize) * TsPacketSize;
        if (packetsByteCount <= 0)
        {
            return (0, SliceFrom(combined, firstSync));
        }

        await destination.WriteAsync(combined, firstSync, packetsByteCount, cancellationToken).ConfigureAwait(false);

        var consumed = firstSync + packetsByteCount;
        var remainder = combined.Length - consumed;
        if (remainder == 0) return (packetsByteCount, Array.Empty<byte>());

        var leftover = new byte[remainder];
        Buffer.BlockCopy(combined, consumed, leftover, 0, remainder);
        return (packetsByteCount, leftover);
    }

    private static byte[] AlignToPacketBoundary(byte[] data)
    {
        var firstSync = IndexOfSync(data, 0);
        if (firstSync < 0) return Array.Empty<byte>();

        var packetsByteCount = ((data.Length - firstSync) / TsPacketSize) * TsPacketSize;
        if (packetsByteCount <= 0) return Array.Empty<byte>();

        var output = new byte[packetsByteCount];
        Buffer.BlockCopy(data, firstSync, output, 0, packetsByteCount);
        return output;
    }

    private static int IndexOfSync(byte[] data, int start)
    {
        for (var i = start; i < data.Length; i++)
        {
            if (data[i] == TsSyncByte) return i;
        }

        return -1;
    }

    private static byte[] SliceFrom(byte[] source, int start)
    {
        var output = new byte[source.Length - start];
        Buffer.BlockCopy(source, start, output, 0, output.Length);
        return output;
    }

    private static byte[] ConcatenateBuffers(byte[] left, byte[] right)
    {
        var combined = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, combined, 0, left.Length);
        Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
        return combined;
    }

    private readonly struct TimeSlice
    {
        public TimeSlice(long startUnix, long endUnix)
        {
            StartUnix = startUnix;
            EndUnix = endUnix;
        }

        public long StartUnix { get; }

        public long EndUnix { get; }
    }

    private sealed class ProgressAggregator
    {
        private readonly IProgress<VideoDownloadProgress>? _sink;
        private readonly long _totalSeconds;
        private readonly object _gate = new();
        private long _bytes;
        private long _packets;

        public ProgressAggregator(IProgress<VideoDownloadProgress>? sink, long totalSeconds)
        {
            _sink = sink;
            _totalSeconds = totalSeconds;
        }

        public void Add(long bytes, long packets)
        {
            if (_sink is null) return;
            long bytesNow;
            long packetsNow;
            lock (_gate)
            {
                _bytes += bytes;
                _packets += packets;
                bytesNow = _bytes;
                packetsNow = _packets;
            }

            _sink.Report(new VideoDownloadProgress(bytesNow, packetsNow, _totalSeconds));
        }
    }
}
