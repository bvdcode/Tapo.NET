using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.MediaStream;

/// <summary>
/// Reads single multipart parts from an underlying <see cref="Stream"/>. The
/// transport produced by the camera is HTTP/1.1-ish but uses ad-hoc multipart
/// framing so we cannot reuse <see cref="System.Net.Http"/>.
/// </summary>
internal sealed class MultipartReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferStart;
    private int _bufferEnd;

    private static readonly byte[] CrLfCrLf = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

    public MultipartReader(Stream stream, int bufferSize = 64 * 1024)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = new byte[bufferSize];
    }

    /// <summary>Reads a single ASCII line terminated by CRLF.</summary>
    public async ValueTask<string> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder(128);
        var sawCr = false;
        while (true)
        {
            await EnsureBytesAsync(1, ct).ConfigureAwait(false);
            var b = _buffer[_bufferStart++];

            if (sawCr)
            {
                if (b == (byte)'\n')
                {
                    return sb.ToString();
                }

                sb.Append('\r');
                sawCr = false;
            }

            if (b == (byte)'\r')
            {
                sawCr = true;
                continue;
            }

            sb.Append((char)b);
        }
    }

    /// <summary>Reads characters until the supplied delimiter is consumed. Bytes before the delimiter are returned.</summary>
    public async ValueTask<byte[]> ReadUntilAsync(byte[] delimiter, CancellationToken ct)
    {
        if (delimiter is null) throw new ArgumentNullException(nameof(delimiter));
        if (delimiter.Length == 0) throw new ArgumentException("Delimiter must not be empty.", nameof(delimiter));

        using var ms = new MemoryStream();
        var matched = 0;

        while (true)
        {
            await EnsureBytesAsync(1, ct).ConfigureAwait(false);
            var b = _buffer[_bufferStart++];

            if (b == delimiter[matched])
            {
                matched++;
                if (matched == delimiter.Length)
                {
                    return ms.ToArray();
                }
            }
            else
            {
                if (matched > 0)
                {
                    ms.Write(delimiter, 0, matched);

                    // Restart matching: account for the case where the byte we
                    // just read could itself begin a new match.
                    matched = b == delimiter[0] ? 1 : 0;
                    if (matched == 0)
                    {
                        ms.WriteByte(b);
                    }
                }
                else
                {
                    ms.WriteByte(b);
                }
            }
        }
    }

    /// <summary>Reads HTTP-style headers up to and including the empty CRLF line.</summary>
    public async ValueTask<Dictionary<string, string>> ReadHeadersAsync(CancellationToken ct)
    {
        var raw = await ReadUntilAsync(CrLfCrLf, ct).ConfigureAwait(false);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var text = Encoding.ASCII.GetString(raw);
        foreach (var line in text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            headers[name] = value;
        }

        return headers;
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes.</summary>
    public async ValueTask<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            if (_bufferStart < _bufferEnd)
            {
                var copy = Math.Min(_bufferEnd - _bufferStart, count - offset);
                Buffer.BlockCopy(_buffer, _bufferStart, buffer, offset, copy);
                _bufferStart += copy;
                offset += copy;
                continue;
            }

            var read = await _stream.ReadAsync(buffer, offset, count - offset, ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The media stream closed before the expected bytes were received.");
            }

            offset += read;
        }

        return buffer;
    }

    private async ValueTask EnsureBytesAsync(int count, CancellationToken ct)
    {
        while (_bufferEnd - _bufferStart < count)
        {
            if (_bufferStart > 0)
            {
                Buffer.BlockCopy(_buffer, _bufferStart, _buffer, 0, _bufferEnd - _bufferStart);
                _bufferEnd -= _bufferStart;
                _bufferStart = 0;
            }

            if (_bufferEnd == _buffer.Length)
            {
                throw new InvalidOperationException("Read buffer is full but the requested data has not been delivered.");
            }

            var read = await _stream.ReadAsync(_buffer, _bufferEnd, _buffer.Length - _bufferEnd, ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of media stream.");
            }

            _bufferEnd += read;
        }
    }
}
