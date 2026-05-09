using System;
using System.Security.Cryptography;
using System.Text;
using Tapo.Internal;

namespace Tapo.Control;

/// <summary>
/// Helpers for the encrypted control transport: AES-CBC over the inner JSON
/// payload plus a SHA-256 tag header that the firmware validates.
/// </summary>
internal static class SecurePassthrough
{
    public static byte[] DeriveToken(string tokenType, string cnonce, string hashedPasswordHex, string nonce)
    {
        if (tokenType is null) throw new ArgumentNullException(nameof(tokenType));

        // Step 1: SHA256(cnonce || hashedPwd || nonce) -> upper hex
        var step1 = Sha256Upper(cnonce + hashedPasswordHex + nonce);

        // Step 2: SHA256(tokenType || cnonce || nonce || step1) -> first 16 bytes
        var step2Bytes = Encoding.UTF8.GetBytes(tokenType + cnonce + nonce + step1);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(step2Bytes);
        var token = new byte[16];
        Buffer.BlockCopy(hash, 0, token, 0, 16);
        return token;
    }

    public static string ComputeRequestTag(string hashedPasswordHex, string cnonce, string requestJson, int seq)
    {
        var firstStage = Sha256Upper(hashedPasswordHex + cnonce);
        var second = Sha256Upper(firstStage + requestJson + seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return second;
    }

    public static string Sha256Upper(string input)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return HexUtilities.ToHexString(hash, upperCase: true);
    }
}
