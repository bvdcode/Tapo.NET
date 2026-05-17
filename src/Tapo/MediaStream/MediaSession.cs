using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tapo.Authentication;
using Tapo.Cryptography;
using Tapo.Internal;

namespace Tapo.MediaStream;

/// <summary>
/// Default implementation of <see cref="IMediaSession"/>. Owns the TCP
/// connection, performs the TPAP handshake, and routes incoming multipart
/// parts to per-request channels.
/// </summary>
public sealed class MediaSession : IMediaSession
{
    private const string ClientBoundary = "--client-stream-boundary--";
    private static readonly byte[] DefaultDeviceBoundary = Encoding.ASCII.GetBytes("--device-stream-boundary--");

    private readonly MediaSessionOptions _options;
    private readonly ConcurrentDictionary<long, Channel<MediaResponse>> _bySession = new();
    private readonly ConcurrentDictionary<long, Channel<MediaResponse>> _bySequence = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private TcpClient? _tcp;
    private Stream? _stream;
    private MultipartReader? _reader;
    private MultipartWriter? _writer;
    private byte[] _deviceBoundary = DefaultDeviceBoundary;
    private AesCbcCipher? _cipher;
    private Task? _responseLoop;
    private long _nextSequence = 1000;

    /// <summary>Creates a new media session for the camera described by <paramref name="options"/>. Call <see cref="StartAsync"/> to connect.</summary>
    public MediaSession(MediaSessionOptions options)
    {
        Throw.IfNull(options);
        Throw.IfNullOrEmpty(options.Host);
        Throw.IfNull(options.CloudPassword);

        _options = options;
        WindowSize = options.WindowSize;
    }

    /// <summary><see langword="true"/> after <see cref="StartAsync"/> has completed successfully.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>Current camera-side flow-control window. See <see cref="MediaSessionOptions.WindowSize"/>.</summary>
    public int WindowSize { get; private set; }

    /// <summary>Overrides <see cref="WindowSize"/>. Must be called before <see cref="StartAsync"/>.</summary>
    public void SetWindowSize(int windowSize)
    {
        if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize));
        if (IsStarted) throw new InvalidOperationException("Window size must be set before the session is started.");
        WindowSize = windowSize;
    }

    /// <summary>Opens the TCP connection, performs the TPAP handshake and starts the response loop. Idempotent.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsStarted) return;

        _tcp = new TcpClient { NoDelay = true };
        var connectTask = _tcp.ConnectAsync(_options.Host, _options.Port);
        if (_options.ConnectTimeoutMilliseconds > 0)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectTimeoutMilliseconds);
            await connectTask.ConfigureAwait(false);
            timeoutCts.Token.ThrowIfCancellationRequested();
        }
        else
        {
            await connectTask.ConfigureAwait(false);
        }

        _stream = _tcp.GetStream();
        if (_options.IoTimeoutMilliseconds > 0)
        {
            _stream.ReadTimeout = _options.IoTimeoutMilliseconds;
            _stream.WriteTimeout = _options.IoTimeoutMilliseconds;
        }

        _reader = new MultipartReader(_stream);
        _writer = new MultipartWriter(_stream, ClientBoundary);

        await PerformHandshakeAsync(cancellationToken).ConfigureAwait(false);

        IsStarted = true;
        _responseLoop = Task.Run(() => ResponseLoopAsync(_shutdownCts.Token));
    }

    /// <summary>Sends a single request and yields each multipart response part as it arrives, until the camera signals end-of-stream.</summary>
    public IAsyncEnumerable<MediaResponse> TransceiveAsync(
        byte[] payload,
        string mimeType = MimeTypes.Json,
        long? sessionId = null,
        bool encrypt = false,
        int noDataTimeoutMilliseconds = 10_000,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(payload);
        Throw.IfNullOrEmpty(mimeType);
        if (!IsStarted) throw new InvalidOperationException("Call StartAsync before transceiving.");

        return TransceiveCore(payload, mimeType, sessionId, encrypt, noDataTimeoutMilliseconds, cancellationToken);
    }

    private async IAsyncEnumerable<MediaResponse> TransceiveCore(
        byte[] payload,
        string mimeType,
        long? sessionId,
        bool encrypt,
        int noDataTimeoutMs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Channel<MediaResponse>? channel = null;
        long? sequence = null;

        if (string.Equals(mimeType, MimeTypes.Json, StringComparison.OrdinalIgnoreCase))
        {
            (payload, sequence) = AssignSequence(payload);
        }

        if (sessionId is not null && _bySession.TryGetValue(sessionId.Value, out var existing))
        {
            channel = existing;
        }
        else if (sequence is not null)
        {
            channel = Channel.CreateBounded<MediaResponse>(new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            _bySequence[sequence.Value] = channel;
        }
        else
        {
            throw new InvalidOperationException("Cannot transceive without an existing session or a sequence number.");
        }

        try
        {
            await SendRequestAsync(payload, mimeType, sessionId, sequence, encrypt, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                MediaResponse response;
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    if (noDataTimeoutMs > 0) timeoutCts.CancelAfter(noDataTimeoutMs);

                    try
                    {
                        response = await channel.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Camera went silent; treat as an end-of-stream marker.
                        yield break;
                    }
                    catch (ChannelClosedException)
                    {
                        yield break;
                    }
                }

                if (response.SessionId is not null && sessionId is null)
                {
                    sessionId = response.SessionId;
                }

                yield return response;
            }
        }
        finally
        {
            if (sequence is not null)
            {
                _bySequence.TryRemove(sequence.Value, out _);
            }

            if (sessionId is not null)
            {
                _bySession.TryRemove(sessionId.Value, out _);
            }
        }
    }

    private (byte[] Payload, long Sequence) AssignSequence(byte[] payload)
    {
        // Decode JSON, inject `seq`, re-encode. We could rewrite the buffer in
        // place but the request side is the cold path — clarity over cleverness.
        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp) ||
            !string.Equals(typeProp.GetString(), "request", StringComparison.OrdinalIgnoreCase))
        {
            return (payload, -1);
        }

        var sequence = Interlocked.Increment(ref _nextSequence) & 0x7FFF;
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "seq", StringComparison.OrdinalIgnoreCase)) continue;
                prop.WriteTo(writer);
            }

            writer.WriteNumber("seq", sequence);
            writer.WriteEndObject();
        }

        return (output.ToArray(), sequence);
    }

    private async Task SendRequestAsync(
        byte[] payload,
        string mimeType,
        long? sessionId,
        long? sequence,
        bool encrypt,
        CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = encrypt ? _cipher!.Encrypt(payload) : payload;

        headers[MediaStreamHeaders.ContentType] = mimeType;
        if (encrypt) headers[MediaStreamHeaders.IsEncrypt] = "1";
        headers[MediaStreamHeaders.ContentLength] = body.Length.ToString(CultureInfo.InvariantCulture);

        if (!string.Equals(mimeType, MimeTypes.Json, StringComparison.OrdinalIgnoreCase))
        {
            headers[MediaStreamHeaders.IsEncrypt] = encrypt ? "1" : "0";
            if (sessionId is not null)
            {
                headers[MediaStreamHeaders.SessionId] = sessionId.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (WindowSize > 0)
        {
            headers[MediaStreamHeaders.DataWindowSize] = WindowSize.ToString(CultureInfo.InvariantCulture);
        }

        await _writeSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer!.WritePartAsync(headers, body, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    private async Task PerformHandshakeAsync(CancellationToken ct)
    {
        var queryString = BuildQueryString(_options.QueryParameters);
        var requestLine = $"POST /stream{queryString} HTTP/1.1";
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MediaStreamHeaders.ContentType] = $"multipart/mixed;boundary={ClientBoundary}",
            [MediaStreamHeaders.Connection] = "keep-alive",
            [MediaStreamHeaders.ContentLength] = "-1",
            [MediaStreamHeaders.Host] = $"{_options.Host}:{_options.Port}",
        };

        if (_options.QueryParameters?.TryGetValue("playerId", out var playerId) == true)
        {
            requestHeaders[MediaStreamHeaders.ClientUuid] = playerId;
        }

        // First request — unauthenticated.
        await _writer!.WriteRequestLineAsync(requestLine, requestHeaders, ct).ConfigureAwait(false);

        var (status, headers) = await ReadStatusAndHeadersAsync(ct).ConfigureAwait(false);
        if (status != 401 || !headers.TryGetValue(MediaStreamHeaders.WwwAuthenticate, out var wwwAuthenticate))
        {
            throw new TapoProtocolException(status);
        }

        // Server sometimes sends a small body alongside the 401 — we ignore it
        // and let the second response provide canonical state.
        if (headers.TryGetValue(MediaStreamHeaders.ContentLength, out var lenStr) &&
            int.TryParse(lenStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var len) && len > 0)
        {
            await _reader!.ReadExactAsync(len, ct).ConfigureAwait(false);
        }

        var challenge = DigestChallenge.Parse(wwwAuthenticate);
        var hashedPassword = PasswordHasher.HashPasswordHex(_options.CloudPassword, _options.EncryptionMethod);
        var cnonce = NonceGenerator.Generate(24);
        var authorization = DigestAuthorization.Build(
            challenge,
            _options.Username,
            hashedPassword,
            method: "POST",
            uri: "/stream",
            cnonce);

        requestHeaders[MediaStreamHeaders.Authorization] = authorization;

        // Second request — authenticated.
        await _writer.WriteRequestLineAsync(requestLine, requestHeaders, ct).ConfigureAwait(false);
        (status, headers) = await ReadStatusAndHeadersAsync(ct).ConfigureAwait(false);
        if (status != 200)
        {
            throw new TapoAuthenticationException($"Camera rejected media credentials with status {status}.");
        }

        if (!headers.TryGetValue(MediaStreamHeaders.KeyExchange, out var keyExchangeHeader))
        {
            throw new KeyExchangeMissingException();
        }

        if (headers.TryGetValue(MediaStreamHeaders.ContentType, out var contentType))
        {
            foreach (var chunk in contentType.Split(';'))
            {
                var trimmed = chunk.Trim();
                if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                {
                    var boundary = trimmed.Substring("boundary=".Length).Trim('"', ' ');
                    if (!string.IsNullOrEmpty(boundary))
                    {
                        _deviceBoundary = Encoding.ASCII.GetBytes(boundary);
                    }
                }
            }
        }

        var keyExchange = KeyExchange.Parse(keyExchangeHeader);
        _cipher = AesCbcCipher.FromKeyExchange(
            keyExchange,
            _options.CloudPassword,
            _options.SuperSecretKey,
            _options.EncryptionMethod);
    }

    private async Task<(int Status, Dictionary<string, string> Headers)> ReadStatusAndHeadersAsync(CancellationToken ct)
    {
        var statusLine = await _reader!.ReadLineAsync(ct).ConfigureAwait(false);
        var status = ParseStatusCode(statusLine);
        var headers = await _reader.ReadHeadersAsync(ct).ConfigureAwait(false);
        return (status, headers);
    }

    private static int ParseStatusCode(string statusLine)
    {
        // Some firmware versions emit "HTTP ERROR 401HTTP/1.0 200 OK". Find the
        // last HTTP/x.y prefix and parse from there to be defensive.
        var idx = statusLine.LastIndexOf("HTTP/", StringComparison.Ordinal);
        var trimmed = idx >= 0 ? statusLine.Substring(idx) : statusLine;
        var parts = trimmed.Split(new[] { ' ' }, 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var status))
        {
            throw new TapoProtocolException($"Failed to parse status line '{statusLine}'.");
        }

        return status;
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return string.Empty;

        var sb = new StringBuilder("?", 64);
        var first = true;
        foreach (var kvp in parameters)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value));
            first = false;
        }

        return sb.ToString();
    }

    private async Task ResponseLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _reader!.ReadUntilAsync(_deviceBoundary, ct).ConfigureAwait(false);
                var headers = await _reader.ReadHeadersAsync(ct).ConfigureAwait(false);

                if (!headers.TryGetValue(MediaStreamHeaders.ContentLength, out var lengthStr) ||
                    !int.TryParse(lengthStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
                {
                    continue;
                }

                var body = await _reader.ReadExactAsync(length, ct).ConfigureAwait(false);

                var encrypted = headers.TryGetValue(MediaStreamHeaders.IsEncrypt, out var encStr)
                    && encStr.Trim() != "0";

                if (encrypted && _cipher is not null)
                {
                    body = _cipher.Decrypt(body);
                }

                long? sequence = null;
                if (headers.TryGetValue(MediaStreamHeaders.DataSequence, out var seqStr) &&
                    long.TryParse(seqStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seqValue))
                {
                    sequence = seqValue;
                }

                long? sessionId = null;
                if (headers.TryGetValue(MediaStreamHeaders.SessionId, out var sessionStr) &&
                    long.TryParse(sessionStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionValue))
                {
                    sessionId = sessionValue;
                }

                var mimeType = headers.TryGetValue(MediaStreamHeaders.ContentType, out var ct2) ? ct2 : string.Empty;
                JsonDocument? json = null;
                Channel<MediaResponse>? finishedFallback = null;

                if (string.Equals(mimeType, MimeTypes.Json, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        json = JsonDocument.Parse(body);
                        ExtractRoutingFromJson(json, ref sequence, ref sessionId, ref finishedFallback);
                    }
                    catch (JsonException)
                    {
                        json = null;
                    }
                }

                var channel = ResolveChannel(sessionId, sequence, finishedFallback);
                if (channel is null)
                {
                    json?.Dispose();
                    continue;
                }

                var response = new MediaResponse(
                    sequence: sequence,
                    sessionId: sessionId,
                    headers: headers,
                    encrypted: encrypted,
                    mimeType: mimeType,
                    payload: body,
                    jsonDocument: json);

                await channel.Writer.WriteAsync(response, ct).ConfigureAwait(false);

                if (sequence is not null && WindowSize > 0 &&
                    sequence.Value > 0 && sequence.Value % WindowSize == 0 && sessionId is not null)
                {
                    await SendAcknowledgementAsync(sessionId.Value, sequence.Value, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception)
        {
            // Surface upstream by completing all channels exceptionally.
            FaultAllChannels();
        }
        finally
        {
            CompleteAllChannels();
        }
    }

    private void ExtractRoutingFromJson(
        JsonDocument doc,
        ref long? sequence,
        ref long? sessionId,
        ref Channel<MediaResponse>? finishedFallback)
    {
        var root = doc.RootElement;
        if (root.TryGetProperty("seq", out var seqElement) && seqElement.TryGetInt64(out var seq))
        {
            sequence = seq;
        }

        if (root.TryGetProperty("params", out var paramsElement))
        {
            if (paramsElement.TryGetProperty("session_id", out var sidElement) && sidElement.TryGetInt64(out var sid))
            {
                sessionId = sid;
            }

            if (root.TryGetProperty("type", out var typeElement) &&
                string.Equals(typeElement.GetString(), "notification", StringComparison.OrdinalIgnoreCase) &&
                paramsElement.TryGetProperty("event_type", out var evtElement) &&
                string.Equals(evtElement.GetString(), "stream_status", StringComparison.OrdinalIgnoreCase) &&
                paramsElement.TryGetProperty("status", out var statusElement) &&
                string.Equals(statusElement.GetString(), "finished", StringComparison.OrdinalIgnoreCase) &&
                _bySession.Count > 0)
            {
                foreach (var pair in _bySession)
                {
                    finishedFallback = pair.Value;
                    break;
                }
            }
        }
    }

    private Channel<MediaResponse>? ResolveChannel(long? sessionId, long? sequence, Channel<MediaResponse>? fallback)
    {
        if (fallback is not null) return fallback;

        if (sessionId is not null && sequence is not null &&
            !_bySession.ContainsKey(sessionId.Value) &&
            _bySequence.TryRemove(sequence.Value, out var promoted))
        {
            _bySession[sessionId.Value] = promoted;
            return promoted;
        }

        if (sessionId is not null && _bySession.TryGetValue(sessionId.Value, out var existing))
        {
            return existing;
        }

        if (sequence is not null && _bySequence.TryGetValue(sequence.Value, out var bySeq))
        {
            return bySeq;
        }

        return null;
    }

    private async Task SendAcknowledgementAsync(long sessionId, long sequence, CancellationToken ct)
    {
        var receivedSoFar = (sequence / WindowSize) * WindowSize;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MediaStreamHeaders.ContentType] = MimeTypes.Json,
            [MediaStreamHeaders.SessionId] = sessionId.ToString(CultureInfo.InvariantCulture),
            [MediaStreamHeaders.DataReceived] = receivedSoFar.ToString(CultureInfo.InvariantCulture),
        };

        var body = Encoding.UTF8.GetBytes(
            "{\"type\":\"notification\",\"params\":{\"event_type\":\"stream_sequence\"}}");
        headers[MediaStreamHeaders.ContentLength] = body.Length.ToString(CultureInfo.InvariantCulture);

        await _writeSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer!.WritePartAsync(headers, body, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    private void CompleteAllChannels()
    {
        foreach (var channel in _bySequence.Values) channel.Writer.TryComplete();
        foreach (var channel in _bySession.Values) channel.Writer.TryComplete();
    }

    private void FaultAllChannels()
    {
        foreach (var channel in _bySequence.Values) channel.Writer.TryComplete(new TapoProtocolException("Media stream interrupted."));
        foreach (var channel in _bySession.Values) channel.Writer.TryComplete(new TapoProtocolException("Media stream interrupted."));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!IsStarted) return;
        IsStarted = false;

        try
        {
            _shutdownCts.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_responseLoop is not null) await _responseLoop.ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        _writeSemaphore.Dispose();
        _shutdownCts.Dispose();
        _cipher?.Dispose();

        try
        {
            _stream?.Dispose();
            _tcp?.Close();
        }
        catch
        {
            // ignore
        }
    }
}
