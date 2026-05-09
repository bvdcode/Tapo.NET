using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tapo.MediaStream;

/// <summary>
/// Writes individual multipart-mixed parts to the underlying connection.
/// The framing follows the camera's quirky convention: a delimiter line of
/// <c>--&lt;boundary&gt;</c>, a block of <c>Name: Value</c> headers, an empty
/// line, the body bytes and a trailing CRLF.
/// </summary>
internal sealed class MultipartWriter
{
    private static readonly byte[] CrLf = { (byte)'\r', (byte)'\n' };

    private readonly Stream _stream;
    private readonly byte[] _delimiterBytes;

    public MultipartWriter(Stream stream, string boundary)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(boundary)) throw new ArgumentException("Boundary required.", nameof(boundary));

        _delimiterBytes = Encoding.ASCII.GetBytes("--" + boundary);
    }

    public async Task WriteRequestLineAsync(string requestLine, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var sb = new StringBuilder(256);
        sb.Append(requestLine).Append("\r\n");
        foreach (var kvp in headers)
        {
            sb.Append(kvp.Key).Append(": ").Append(kvp.Value).Append("\r\n");
        }

        sb.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        await _stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Writes a multipart part with the supplied headers and body.</summary>
    public async Task WritePartAsync(
        IReadOnlyDictionary<string, string> headers,
        byte[] body,
        CancellationToken ct)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));

        await _stream.WriteAsync(_delimiterBytes, 0, _delimiterBytes.Length, ct).ConfigureAwait(false);
        await _stream.WriteAsync(CrLf, 0, CrLf.Length, ct).ConfigureAwait(false);

        var headerBuilder = new StringBuilder(128);
        foreach (var kvp in headers)
        {
            headerBuilder.Append(kvp.Key).Append(": ").Append(kvp.Value).Append("\r\n");
        }

        headerBuilder.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(headerBuilder.ToString());
        await _stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct).ConfigureAwait(false);

        if (body.Length > 0)
        {
            await _stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        }

        await _stream.WriteAsync(CrLf, 0, CrLf.Length, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
