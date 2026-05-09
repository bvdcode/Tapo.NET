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

    public string Realm { get; }

    public string Nonce { get; }

    public string Qop { get; }

    public string? Opaque { get; }

    public string Algorithm { get; }

    public static DigestChallenge Parse(string headerValue)
    {
        Throw.IfNullOrEmpty(headerValue);

        // Strip the leading "Digest " scheme name if present.
        const string Scheme = "Digest";
        var trimmed = headerValue.TrimStart();
        if (trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(Scheme.Length).TrimStart();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SplitTopLevelCommas(trimmed))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            var name = pair.Substring(0, eq).Trim();
            var rawValue = pair.Substring(eq + 1).Trim();
            if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[rawValue.Length - 1] == '"')
            {
                rawValue = rawValue.Substring(1, rawValue.Length - 2);
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
                yield return input.Substring(start, i - start);
                start = i + 1;
            }
        }

        if (start < input.Length)
        {
            yield return input.Substring(start);
        }
    }
}
