using System;
using System.Collections.Generic;
using Tapo.Internal;

namespace Tapo.Cryptography;

/// <summary>
/// Parsed payload of the camera's <c>Key-Exchange</c> response header. The
/// camera echoes a <c>username</c> and a per-session <c>nonce</c>; both are
/// fed into the AES key derivation.
/// </summary>
public readonly struct KeyExchange
{
    /// <summary>Creates a parsed Key-Exchange pair from the supplied fields.</summary>
    public KeyExchange(string username, string nonce)
    {
        Throw.IfNullOrEmpty(username);
        Throw.IfNullOrEmpty(nonce);

        Username = username;
        Nonce = nonce;
    }

    /// <summary>Username echoed by the camera — <c>"none"</c> when encryption is disabled.</summary>
    public string Username { get; }

    /// <summary>Per-session nonce used as the AES key derivation input.</summary>
    public string Nonce { get; }

    /// <summary>
    /// Parses a header value of the form <c>username="..." nonce="..."</c>.
    /// </summary>
    public static KeyExchange Parse(string headerValue)
    {
        Throw.IfNullOrEmpty(headerValue);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in headerValue.Split(' '))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim().Trim('"');
            values[key] = value;
        }

        if (!values.TryGetValue("username", out var user) ||
            !values.TryGetValue("nonce", out var nonce))
        {
            throw new FormatException("Key-Exchange header is missing username or nonce.");
        }

        return new KeyExchange(user, nonce);
    }
}
