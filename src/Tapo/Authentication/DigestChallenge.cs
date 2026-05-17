using System;
using System.Collections.Generic;
using Tapo.Internal;

namespace Tapo.Authentication;

/// <summary>
/// Parsed <c>WWW-Authenticate: Digest ...</c> challenge issued by the camera
/// during the media-stream handshake.
/// </summary>
public sealed class DigestChallenge
{
    private DigestChallenge(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("realm", out var realm)) throw new FormatException("Digest challenge missing realm.");
        if (!values.TryGetValue("nonce", out var nonce)) throw new FormatException("Digest challenge missing nonce.");

        Realm = realm;
        Nonce = nonce;
        Qop = values.TryGetValue("qop", out var qop) ? qop : "auth";
        Opaque = values.TryGetValue("opaque", out var opaque) ? opaque : null;
        Algorithm = values.TryGetValue("algorithm", out var algorithm) ? algorithm : "MD5";
    }

    /// <summary>
    /// Realm advertised by the camera. The Tapo media-stream handshake requires this value to be included in the digest response calculation, but does not specify any particular format or content for it.
    /// </summary>
    public string Realm { get; }

    /// <summary>
    /// Nonce value advertised by the camera. This is a random string that should be used as part of the digest response calculation to prevent replay attacks. The Tapo media-stream handshake requires this value to be included in the digest response, but does not specify any particular format or length for it.
    /// </summary>
    public string Nonce { get; }

    /// <summary>
    /// Quality of Protection advertised by the camera. The Tapo media-stream handshake only supports "auth", but we parse this value for completeness and future compatibility.
    /// </summary>
    public string Qop { get; }

    /// <summary>
    /// Opaque value advertised by the camera. The Tapo media-stream handshake does not use this value, but we parse it for completeness and future compatibility.
    /// </summary>
    public string? Opaque { get; }

    /// <summary>
    /// Hash algorithm advertised by the camera. The Tapo media-stream handshake only supports "MD5", but we parse this value for completeness and future compatibility.
    /// </summary>
    public string Algorithm { get; }

    /// <summary>
    /// Parses a <c>WWW-Authenticate: Digest ...</c> header value into a <see cref="DigestChallenge"/> instance.
    /// </summary>
    /// <param name="headerValue">The raw header value, including the leading "Digest" scheme name.</param>
    /// <returns>A <see cref="DigestChallenge"/> instance containing the parsed values.</returns>
    public static DigestChallenge Parse(string headerValue)
    {
        Throw.IfNullOrEmpty(headerValue);

        // Strip the leading "Digest " scheme name if present.
        const string Scheme = "Digest";
        var trimmed = headerValue.TrimStart();
        if (trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[Scheme.Length..].TrimStart();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SplitTopLevelCommas(trimmed))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            var name = pair[..eq].Trim();
            var rawValue = pair[(eq + 1)..].Trim();
            if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"')
            {
                rawValue = rawValue[1..^1];
            }

            values[name] = rawValue;
        }

        return new DigestChallenge(values);
    }

    private static IEnumerable<string> SplitTopLevelCommas(string input)
    {
        var inQuotes = false;
        var start = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                yield return input[start..i];
                start = i + 1;
            }
        }

        if (start < input.Length)
        {
            yield return input[start..];
        }
    }
}
