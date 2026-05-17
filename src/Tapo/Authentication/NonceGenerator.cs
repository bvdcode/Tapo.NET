using System;
using System.Security.Cryptography;
using System.Text;
using Tapo.Internal;

namespace Tapo.Authentication;

/// <summary>
/// Cryptographically random hex-encoded nonces. The Python reference uses
/// <c>os.urandom(n).hex()</c>, so the resulting string is twice as long as the
/// requested byte length.
/// </summary>
public static class NonceGenerator
{
    /// <summary>Generates a random hex-encoded nonce of <paramref name="byteLength"/> random bytes (so 2× as many hex characters).</summary>
    public static string Generate(int byteLength = 24)
    {
        if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));

        Span<byte> buffer = byteLength <= 64 ? stackalloc byte[byteLength] : new byte[byteLength];
        using (var rng = RandomNumberGenerator.Create())
        {
            var owned = buffer.ToArray();
            rng.GetBytes(owned);
            return HexUtilities.ToHexString(owned);
        }
    }

    /// <summary>
    /// Same as <see cref="Generate"/> but returns the nonce as ASCII bytes —
    /// avoids an extra UTF-8 encode when the caller intends to feed it
    /// straight into a hash.
    /// </summary>
    public static byte[] GenerateAscii(int byteLength = 24)
        => Encoding.ASCII.GetBytes(Generate(byteLength));
}
