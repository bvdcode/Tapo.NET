using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tapo.Internal;

namespace Tapo.Authentication;

/// <summary>
/// Builds an HTTP Digest <c>Authorization</c> header value compatible with the
/// firmware's MD5-only verifier. Algorithm: <c>HA1 = MD5(user:realm:hashed_pwd)</c>,
/// <c>HA2 = MD5(method:uri)</c>, <c>response = MD5(HA1:nonce:nc:cnonce:qop:HA2)</c>.
/// </summary>
public static class DigestAuthorization
{
    /// <summary>
    /// Builds the value of the <c>Authorization</c> header for a request that
    /// matches an earlier 401 challenge.
    /// </summary>
    public static string Build(
        DigestChallenge challenge,
        string username,
        string hashedPasswordHex,
        string method,
        string uri,
        string cnonce,
        int nonceCount = 1)
    {
        Throw.IfNull(challenge);
        Throw.IfNullOrEmpty(username);
        Throw.IfNullOrEmpty(hashedPasswordHex);
        Throw.IfNullOrEmpty(method);
        Throw.IfNullOrEmpty(uri);
        Throw.IfNullOrEmpty(cnonce);

        var nc = nonceCount.ToString("x8", CultureInfo.InvariantCulture);

        var ha1 = Md5Hex($"{username}:{challenge.Realm}:{hashedPasswordHex}");
        var ha2 = Md5Hex($"{method}:{uri}");
        var response = Md5Hex(string.Join(":",
            ha1,
            challenge.Nonce,
            nc,
            cnonce,
            challenge.Qop,
            ha2));

        var sb = new StringBuilder(256);
        sb.Append("Digest username=\"").Append(username).Append('"');
        sb.Append(",realm=\"").Append(challenge.Realm).Append('"');
        sb.Append(",uri=\"").Append(uri).Append('"');
        sb.Append(",algorithm=").Append(challenge.Algorithm);
        sb.Append(",nonce=\"").Append(challenge.Nonce).Append('"');
        sb.Append(",nc=").Append(nc);
        sb.Append(",cnonce=\"").Append(cnonce).Append('"');
        sb.Append(",qop=").Append(challenge.Qop);
        sb.Append(",response=\"").Append(response).Append('"');
        if (!string.IsNullOrEmpty(challenge.Opaque))
        {
            sb.Append(",opaque=\"").Append(challenge.Opaque).Append('"');
        }

        return sb.ToString();
    }

    private static string Md5Hex(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return HexUtilities.ToHexString(hash);
    }
}
